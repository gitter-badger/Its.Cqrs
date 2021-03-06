// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Microsoft.Its.Domain
{
    public static class CommandSchedulerExtensions
    {
        // TODO: (CommandSchedulerExtensions) combine with CommandScheduler class
        public static async Task<IScheduledCommand<TAggregate>> Schedule<TCommand, TAggregate>(
            this ICommandScheduler<TAggregate> scheduler,
            Guid aggregateId,
            TCommand command,
            DateTimeOffset? dueTime = null,
            IEvent deliveryDependsOn = null)
            where TCommand : ICommand<TAggregate>
            where TAggregate : IEventSourced
        {
            if (aggregateId == Guid.Empty)
            {
                throw new ArgumentException("Parameter aggregateId cannot be an empty Guid.");
            }

            var scheduledCommand = CommandScheduler.CreateScheduledCommand<TCommand, TAggregate>(
                aggregateId,
                command,
                dueTime,
                deliveryDependsOn);

            await scheduler.Schedule(scheduledCommand);

            return scheduledCommand;
        }

        internal static void DeliverIfPreconditionIsSatisfiedWithin<TAggregate>(
            this ICommandScheduler<TAggregate> scheduler,
            TimeSpan timespan,
            IScheduledCommand<TAggregate> scheduledCommand,
            IEventBus eventBus) where TAggregate : IEventSourced
        {
            eventBus.Events<IEvent>()
                    .Where(
                        e => e.AggregateId == scheduledCommand.DeliveryPrecondition.AggregateId &&
                             e.ETag == scheduledCommand.DeliveryPrecondition.ETag)
                    .Take(1)
                    .Timeout(timespan)
                    .Subscribe(
                        e => { Task.Run(() => scheduler.Deliver(scheduledCommand)).Wait(); },
                        onError: ex => { eventBus.PublishErrorAsync(new EventHandlingError(ex, scheduler)); });
        }
    }
}