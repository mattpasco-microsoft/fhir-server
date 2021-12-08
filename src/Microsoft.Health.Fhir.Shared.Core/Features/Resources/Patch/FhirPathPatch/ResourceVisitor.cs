// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Hl7.Fhir.Language;
using fhirExpression = Hl7.FhirPath.Expressions;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch
{
    public class ResourceVisitor : fhirExpression.ExpressionVisitor<Expression>
    {
        private readonly ParameterExpression _parameter;

        public ResourceVisitor(ParameterExpression parameter)
        {
            _parameter = parameter;
        }

        /// <inheritdoc />
        public override Expression VisitConstant(
            fhirExpression.ConstantExpression expression,
            fhirExpression.SymbolTable scope)
        {
            if (expression.ExpressionType == TypeSpecifier.Integer)
            {
                return Expression.Constant((int)expression.Value);
            }

            if (expression.ExpressionType == TypeSpecifier.String)
            {
                var propertyName = expression.Value.ToString();
                var property = GetProperty(_parameter.Type, propertyName);
                return property == null
                    ? _parameter
                    : Expression.Property(_parameter, property);
            }

            return null;
        }

        /// <inheritdoc />
        public override Expression VisitFunctionCall(
            fhirExpression.FunctionCallExpression expression,
            fhirExpression.SymbolTable scope)
        {
            switch (expression)
            {
                case fhirExpression.IndexerExpression indexerExpression:
                    {
                        var index = indexerExpression.Index.Accept(this, scope);
                        var property = indexerExpression.Focus.Accept(this, scope);
                        var itemProperty = GetProperty(property.Type, "Item");
                        return Expression.MakeIndex(property, itemProperty, new[] { index });
                    }

                case fhirExpression.ChildExpression child:
                    {
                        return child.Arguments.First().Accept(this, scope);
                    }

                default:
                    return _parameter;
            }
        }

        /// <inheritdoc />
        public override Expression VisitNewNodeListInit(
            fhirExpression.NewNodeListInitExpression expression,
            fhirExpression.SymbolTable scope)
        {
            return _parameter;
        }

        /// <inheritdoc />
        public override Expression VisitVariableRef(fhirExpression.VariableRefExpression expression, fhirExpression.SymbolTable scope)
        {
            return _parameter;
        }

        private static PropertyInfo GetProperty(Type constantType, string propertyName)
        {
            var propertyInfos = constantType.GetProperties();
            var property =
                propertyInfos.FirstOrDefault(p => p.Name.Equals(propertyName + "Element", StringComparison.OrdinalIgnoreCase))
                ?? propertyInfos.FirstOrDefault(x => x.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

            return property;
        }
    }
}
