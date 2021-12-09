﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FhirPathPatch.Operations;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch
{
    /// <summary>
    /// Handles patching a FHIR Resource in a builder pattern manner.
    /// </summary>
    public class FhirPathPatchBuilder
    {
        private Resource resource;

        private readonly List<PendingOperation> operations;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPathPatchBuilder"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource.</param>
        public FhirPathPatchBuilder(Resource resource)
        {
            this.resource = resource;
            operations = new List<PendingOperation>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPathPatchBuilder"/> class with Patch Parameters.
        /// </summary>
        /// <param name="resource">FHIR Resource.</param>
        /// <param name="parameters">Patch Parameters.</param>
        public FhirPathPatchBuilder(Resource resource, Parameters parameters)
        : this(resource)
        {
            Build(parameters);
        }

        /// <summary>
        /// Applies the list of pending operations to the resource.
        /// </summary>
        /// <returns>FHIR Resource <see cref="Resource"/>.</returns>
        public Resource Apply()
        {
            foreach (var po in operations)
            {
                resource = po.Type switch
                {
                    EOperationType.ADD => new OperationAdd(resource, po).Execute(),
                    EOperationType.INSERT => new OperationInsert(resource, po).Execute(),
                    EOperationType.REPLACE => new OperationReplace(resource, po).Execute(),
                    EOperationType.DELETE => new OperationDelete(resource, po).Execute(),
                    EOperationType.MOVE => new OperationMove(resource, po).Execute(),
                    _ => throw new NotImplementedException(),
                };
            }

            return resource;
        }

        /// <summary>
        /// Handles the add operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Add(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the insert operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Insert(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the delete operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Delete(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the Replace operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Replace(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the Move operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Move(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Builds the list of operations to execute
        /// </summary>
        /// <param type="parameters" name="parameters">The parameters to build the chain of the builder.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Build(Parameters parameters)
        {
            foreach (var param in parameters.Parameter)
            {
                operations.Add(PendingOperation.FromParameterComponent(param));
            }

            return this;
        }
    }
}
