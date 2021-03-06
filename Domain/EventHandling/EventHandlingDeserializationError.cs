// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Dynamic;

namespace Microsoft.Its.Domain
{
    [DebuggerStepThrough]
    internal class EventHandlingDeserializationError : EventHandlingError, IHaveExtensibleMetada
    {
        private readonly string body;
        private ExpandoObject metadata;

        public EventHandlingDeserializationError(
            Exception exception,
            string body,
            Guid aggregateId,
            long sequenceNumber,
            DateTimeOffset timestamp,
            string actor,
            string streamName,
            string type)
            : base(exception)
        {
            Timestamp = timestamp;
            Actor = actor;
            StreamName = streamName;
            Type = type;
            this.body = body;
            AggregateId = aggregateId;
            SequenceNumber = sequenceNumber;
        }

        public string Actor { get; private set; }

        public string Body
        {
            get
            {
                return body;
            }
        }

        public dynamic Metadata
        {
            get
            {
                return metadata ?? (metadata = new ExpandoObject());
            }
        }

        public DateTimeOffset Timestamp { get; private set; }

        public string Type { get; private set; }
    }
}
