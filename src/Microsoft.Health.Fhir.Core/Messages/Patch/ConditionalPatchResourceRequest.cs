﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public sealed class ConditionalPatchResourceRequest : ConditionalResourceRequest<UpsertResourceResponse>
    {
        private static readonly string[] Capabilities = new string[1] { "conditionalPatch = true" };

        public ConditionalPatchResourceRequest(
            string resourceType,
            IPatchPayload payload,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            WeakETag weakETag = null)
            : base(resourceType, conditionalParameters)
        {
            EnsureArg.IsNotNull(payload, nameof(payload));

            Payload = payload;
            WeakETag = weakETag;
        }

        public IPatchPayload Payload { get; }

        public WeakETag WeakETag { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
