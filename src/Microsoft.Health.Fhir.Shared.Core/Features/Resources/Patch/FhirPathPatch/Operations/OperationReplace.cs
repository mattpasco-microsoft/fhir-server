// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires replacing an element from the resource.
    /// </summary>
    public class OperationReplace : OperationBase, IOperation
    {
        public OperationReplace(Resource resource)
            : base(resource)
        {
        }

        /// <summary>
        /// Executes a FHIRPath Patch Replace operation. Add operations will
        /// replace the element located at operation.Path with a new value
        /// specified at operation.Value.
        ///
        /// Fhir package has a built-in "ReplaceWith" operation which
        /// accomplishes this easily.
        /// </summary>
        /// <param name="operation">PendingOperation representing Replace operation.</param>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        public override Resource Execute(PendingOperation operation)
        {
            base.Execute(operation);

            var targetElement = ResourceElement.Find(operation.Path);
            targetElement.ReplaceWith(PocoProvider, ValueElement);

            return ResourceElement.ToPoco<Resource>();
        }
    }
}
