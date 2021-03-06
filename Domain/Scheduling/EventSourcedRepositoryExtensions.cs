// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Its.Recipes;

namespace Microsoft.Its.Domain
{
    internal static class EventSourcedRepositoryExtensions
    {
        internal const int DefaultNumberOfRetriesOnException = 5;

        private static readonly MethodInfo createMethod = typeof (CommandFailed)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == "Create");

        public static async Task ApplyScheduledCommand<TAggregate>(
            this IEventSourcedRepository<TAggregate> repository,
            IScheduledCommand<TAggregate> scheduled,
            ICommandPreconditionVerifier preconditionVerifier = null)
            where TAggregate : class, IEventSourced
        {
            TAggregate aggregate = null;
            Exception exception = null;

            if (scheduled.Result is CommandDelivered)
            {
               return;
            }

            try
            {
                if (preconditionVerifier != null &&
                    !await preconditionVerifier.IsPreconditionSatisfied(scheduled))
                {
                    await FailScheduledCommand(repository,
                                               scheduled,
                                               new PreconditionNotMetException(scheduled.DeliveryPrecondition));
                    return;
                }

                aggregate = await repository.GetLatest(scheduled.AggregateId);

                if (aggregate == null)
                {
                    if (scheduled.Command is ConstructorCommand<TAggregate>)
                    {
                        var ctor = typeof (TAggregate).GetConstructor(new[] { scheduled.Command.GetType() });
                        aggregate = (TAggregate) ctor.Invoke(new[] { scheduled.Command });
                    }
                    else
                    {
                        // TODO: (ApplyScheduledCommand) this should probably be a different exception type.
                        throw new ConcurrencyException(
                            string.Format("No {0} was found with id {1} so the command could not be applied.",
                                          typeof (TAggregate).Name, scheduled.AggregateId),
                            new IEvent[] { scheduled });
                    }
                }
                else
                {
                    await aggregate.ApplyAsync(scheduled.Command);
                }

                await repository.Save(aggregate);

                scheduled.Result = new CommandSucceeded(scheduled);

                return;
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            await FailScheduledCommand(repository, scheduled, exception, aggregate);
        }

        private static async Task FailScheduledCommand<TAggregate>(
            IEventSourcedRepository<TAggregate> repository,
            IScheduledCommand<TAggregate> scheduled,
            Exception exception = null,
            TAggregate aggregate = null)
            where TAggregate : class, IEventSourced
        {
            var failure = (CommandFailed) createMethod
                .MakeGenericMethod(scheduled.Command.GetType())
                .Invoke(null, new object[] { scheduled.Command, scheduled, exception });

            var previousAttempts = scheduled.IfHas<int>(s => s.Metadata.NumberOfPreviousAttempts)
                                            .ElseDefault();

            failure.NumberOfPreviousAttempts = previousAttempts;

            if (aggregate != null)
            {
                // TODO: (FailScheduledCommand) refactor so that getting hold of the handler is simpler
                var scheduledCommandOfT = scheduled.Command as Command<TAggregate>;
                if (scheduledCommandOfT != null)
                {
                    if (scheduledCommandOfT.Handler != null)
                    {
                        await scheduledCommandOfT.Handler
                                                 .HandleScheduledCommandException((dynamic) aggregate,
                                                                                  (dynamic) failure);
                    }
                }

                if (!(exception is ConcurrencyException))
                {
                    try
                    {
                        await repository.Save(aggregate);
                    }
                    catch (Exception ex)
                    {
                        // TODO: (FailScheduledCommand) surface this more clearly
                        Trace.Write(ex);
                    }
                }
                else if (scheduled.Command is ConstructorCommand<TAggregate>)
                {
                    failure.Cancel();
                    scheduled.Result = failure;
                    return;
                }
            }

            if (!failure.IsCanceled &&
                failure.RetryAfter == null &&
                failure.NumberOfPreviousAttempts < DefaultNumberOfRetriesOnException)
            {
                failure.Retry(TimeSpan.FromMinutes(Math.Pow(failure.NumberOfPreviousAttempts + 1, 2)));
            }

            scheduled.Result = failure;
        }
    }
}