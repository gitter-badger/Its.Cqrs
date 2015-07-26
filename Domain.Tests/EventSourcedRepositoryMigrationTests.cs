// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Sample.Domain.Ordering;
using Sample.Domain.Ordering.Commands;

namespace Microsoft.Its.Domain.Tests
{
    public abstract class EventMigrationTests
    {
        private IEventSourcedRepository<Order> repository;
        private Guid aggregateId;

        protected abstract IEventSourcedRepository<Order> CreateRepository();

        [SetUp]
        public void SetUp()
        {
            Command<Order>.AuthorizeDefault = delegate { return true; };
            repository = CreateRepository();

            var order = new Order().Apply(new AddItem
            {
                ProductName = "Widget",
                Price = 10m,
                Quantity = 2
            });
            repository.Save(order).Wait();
            aggregateId = order.Id;

            repository.GetLatest(aggregateId).Result.EventHistory.Last().Should().BeOfType<Order.ItemAdded>();
        }

        [Test]
        public async Task If_renamed_and_saved_When_the_aggregate_is_sourced_then_the_event_is_the_new_name()
        {
            var order = await repository.GetLatest(aggregateId);
            var rename = new EventMigrator.Rename(order.EventHistory.Last().SequenceNumber, "ItemAdded2");

            await EventMigrator.SaveWithRenames(MigratableRepository, order, new[] { rename });
            (await repository.GetLatest(aggregateId)).EventHistory.Last().Should().BeOfType<Order.ItemAdded2>();
        }

        private IMigratableEventSourcedRepository<Order> MigratableRepository
        {
            get { return (IMigratableEventSourcedRepository<Order>)repository; }
        }

        [Test]
        public async Task If_renamed_but_not_saved_When_the_aggregate_is_sourced_then_the_event_is_the_old_name()
        {
            var order = await repository.GetLatest(aggregateId);
            var rename = new EventMigrator.Rename(order.EventHistory.Last().SequenceNumber, "ItemAdded2");

            (await repository.GetLatest(aggregateId)).EventHistory.Last().Should().BeOfType<Order.ItemAdded>();
        }

        [Test]
        public async Task If_renamed_to_an_unknown_name_When_the_aggregate_is_sourced_then_the_event_is_anonymous()
        {
            var order = await repository.GetLatest(aggregateId);
            var rename = new EventMigrator.Rename(order.EventHistory.Last().SequenceNumber, "ItemAdded (ignored)");

            await EventMigrator.SaveWithRenames(MigratableRepository, order, new[] { rename });
            (await repository.GetLatest(aggregateId)).EventHistory.Last().GetType().Name.Should().Be("AnonymousEvent`1");
        }

        [Test]
        public async Task If_an_unrecognized_event_is_renamed_then_a_useful_exception_is_thrown()
        {
            var order = await repository.GetLatest(aggregateId);
            var rename = new EventMigrator.Rename(99999, "ItemAdded (ignored)");
            Func<Task> action = () => EventMigrator.SaveWithRenames(MigratableRepository, order, new[] { rename });

            action.ShouldThrow<EventMigrator.SequenceNumberNotFoundException>()
                  .And.Message.Should().StartWith("Migration failed, because no event with sequence number 99999 on aggregate ");
        }
    }
}