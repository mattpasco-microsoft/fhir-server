// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch;
using static Hl7.Fhir.Model.Parameters;
using fhirExpression = Hl7.FhirPath.Expressions;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// Abstract representation of a basic operational resource.
    /// </summary>
    public abstract class OperationBase : IOperation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OperationBase"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource for this operation.</param>
        protected OperationBase(Resource resource)
        {
            PocoProvider = new PocoStructureDefinitionSummaryProvider();
            Resource = resource;
            ResourceElement = resource.ToElementNode();
        }

        /// <summary>
        /// Gets the provider used in POCO conversion.
        /// </summary>
        protected PocoStructureDefinitionSummaryProvider PocoProvider { get; }

        /// <summary>
        /// Gets the element node representation of the patch operation
        /// resource.
        /// </summary>
        protected ElementNode ResourceElement { get; }

        protected Resource Resource { get; }

        protected ElementNode ValueElement { get; set; }

        /// <summary>
        /// All inheriting classes must implement an operation exeuction method.
        /// </summary>
        /// <param name="operation">Input parameters to Patch operatioin.</param>
        /// <returns>FHIR Resource as POCO.</returns>
        public virtual Resource Execute(PendingOperation operation)
        {
            if (this is OperationDelete || this is OperationMove)
            {
                return Resource;
            }

            var compiler = new FhirPathCompiler(FhirPathCompiler.DefaultSymbolTable);
            var path = operation.Path + (this is OperationAdd ? $".{operation.Name}" : string.Empty);
            var expression = compiler.Parse(path);
            var compiledExpression = compiler.Parse(path);
            var parameterExpression = System.Linq.Expressions.Expression.Parameter(Resource.GetType(), "x");

            var result = expression.Accept(
                        new ResourceVisitor(parameterExpression),
                        new fhirExpression.SymbolTable());

            // var resultCompiled = compiler.Compile(result);

            var resultType = result.Type;
            if (result is MemberExpression mem)
            {
                var declared = mem.Member.GetCustomAttribute<DeclaredTypeAttribute>();
                if (declared is not null)
                {
                    resultType = declared.Type;
                }
            }

            ValueElement = GetElementNodeFromPart(operation.Value, resultType);
            return Resource;
        }

        private static ElementNode GetElementNodeFromPart(ParameterComponent part, Type resultType)
        {
            if (part.Value is null)
            {
                var provider = new PocoStructureDefinitionSummaryProvider();
                var node = ElementNode.Root(provider, resultType.ToString());
                var properties = GetPartsBindings(part.Part, resultType);

                foreach (var property in properties)
                {
                    node.Add(provider, property.Item2, property.Item1);
                }

                return node;
            }

            if (part.Value is DataType valueDataType)
            {
                if (resultType.IsAssignableFrom(valueDataType.GetType()))
                {
                    return valueDataType.ToElementNode();
                }

                // ElementNode.ForPrimative?
                // TypedElementParseExtensions.ParseBiedable
            }

            throw new InvalidOperationException();
        }

        // Anon objects
        // https://github.com/FirelyTeam/spark/blob/795d79e059c751d029257ec6d22da5be850ee016/src/Spark.Engine/Service/FhirServiceExtensions/PatchService.cs#L102
        private static IEnumerable<Tuple<string, ElementNode>> GetPartsBindings(List<ParameterComponent> parts, Type resultType)
        {
            var rtn = new List<Tuple<string, ElementNode>>();

            foreach (var partGroup in parts.GroupBy(x => x.Name))
            {
                if (resultType.IsGenericType)
                {
                    resultType = resultType.GenericTypeArguments.First();
                }

                var propertyInfo = resultType.GetProperties().Single(
                    p => p.GetCustomAttribute<FhirElementAttribute>()?.Name == partGroup.Key);
                ElementNode propertyValue = GetElementNodeFromPart(partGroup.Single(), propertyInfo.PropertyType);
                rtn.Add(new Tuple<string, ElementNode>(propertyInfo.Name, propertyValue));
            }

            return rtn;
        }
    }
}
