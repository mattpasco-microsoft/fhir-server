// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FhirPathPatch.Operations;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Language;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using static Hl7.Fhir.Model.Parameters;
using Expression = System.Linq.Expressions.Expression;
using fhirExpression = Hl7.FhirPath.Expressions;

namespace FhirPathPatch
{
    /// <summary>
    /// Handles patching a FHIR Resource in a builder pattern manner.
    /// </summary>
    public class FhirPathPatchBuilder
    {
        private Resource resource;

        private List<PendingOperation> operations;

        private readonly FhirPathCompiler compiler;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPathPatchBuilder"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource.</param>
        public FhirPathPatchBuilder(Resource resource)
        {
            this.resource = resource;

            this.operations = new List<PendingOperation>();

            this.compiler = new FhirPathCompiler();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPathPatchBuilder"/> class with Patch Parameters.
        /// </summary>
        /// <param name="resource">FHIR Resource.</param>
        /// <param name="parameters">Patch Parameters.</param>
        public FhirPathPatchBuilder(Resource resource, Parameters parameters)
        : this(resource)
        {
            this.Build(parameters);
        }

        /// <summary>
        /// Builds the list of operations to execute
        /// </summary>
        /// <param type="parameters" name="parameters">The parameters to build the chain of the builder.</param>
        public FhirPathPatchBuilder Build(Parameters parameters)
        {
            foreach (var param in parameters.Parameter)
            {
                this.operations.Add(PendingOperation.FromParameterComponent(param));
            }

            return this;
        }

        public FhirPathPatchBuilder Add(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Insert(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Delete(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Replace(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Move(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        /// <summary>
        /// Applies the list of pending operations to the resource.
        /// </summary>
        public Resource Apply()
        {
            foreach (var po in this.operations)
            {
                var parameterExpression = Expression.Parameter(resource.GetType(), "x");
                var expression = po.Type == EOperationType.ADD ? this.compiler.Parse($"{po.Path}.{po.Name}") : this.compiler.Parse(po.Path);
                var result = expression.Accept(
                        new ResourceVisitor(parameterExpression),
                        new fhirExpression.SymbolTable());
                switch (po.Type)
                {
                    case EOperationType.ADD:
                        result = AddValue(result, CreateValueExpression(po.Value, result.Type));
                        break;
                    case EOperationType.INSERT:
                        var insertIndex = po.Index;
                        result = InsertValue(result, CreateValueExpression(po.Value, result.Type), insertIndex ?? -1);
                        break;
                    case EOperationType.REPLACE:
                        result = Expression.Assign(result, CreateValueExpression(po.Value, result.Type));
                        break;
                    case EOperationType.DELETE:
                        result = DeleteValue(result);
                        break;
                    case EOperationType.MOVE:
                        result = MoveItem(result, po.Source ?? -1, po.Destination ?? -1);
                        break;
                }

                var compiled = Expression.Lambda(result!, parameterExpression).Compile();
                compiled.DynamicInvoke(resource);
            }

            return resource;
        }

        private static Expression CreateValueExpression(object data, Type resultType)
        {
            resultType = data is List<ParameterComponent> && resultType.IsGenericType ? resultType.GenericTypeArguments[0] : resultType;

            if (data is List<ParameterComponent>)
            {
                return Expression.MemberInit(
                    Expression.New(resultType.GetConstructor(Array.Empty<Type>())),
                    GetPartsBindings((List<ParameterComponent>)data, resultType));
            }
            else if (data is DataType)
            {
                return GetConstantExpression((DataType)data, resultType);
            }

            throw new ArgumentException("Data must be of type List<ParameterComponents> or DataType");
        }

        private static Expression GetConstantExpression(DataType value, Type valueType)
        {
            Expression FromString(string str, Type targetType)
            {
                return targetType.CanBeTreatedAsType(typeof(DataType))
                    ? Expression.MemberInit(
                        Expression.New(targetType.GetConstructor(Array.Empty<Type>())),
                        Expression.Bind(
                            targetType.GetProperty("ObjectValue"),
                            Expression.Constant(str)))
                    : Expression.Constant(str);
            }

            return value switch
            {
                Code code => FromString(code.Value, valueType == typeof(DataType) ? typeof(Code) : valueType),
                FhirUri uri => FromString(uri.Value, valueType == typeof(DataType) ? typeof(FhirUri) : valueType),
                FhirString s => FromString(s.Value, valueType == typeof(DataType) ? typeof(FhirString) : valueType),
                _ => Expression.Constant(value),
            };
        }

        private static IEnumerable<MemberBinding> GetPartsBindings(List<Parameters.ParameterComponent> parts, Type resultType)
        {
            foreach (var partGroup in parts.GroupBy(x => x.Name))
            {
                var property = resultType.GetProperties().Single(
                    p => p.GetCustomAttribute<FhirElementAttribute>()?.Name == partGroup.Key);
                if (property.PropertyType.IsGenericType)
                {
                    var listExpression = GetCollectionExpression(property, partGroup);
                    yield return Expression.Bind(property, listExpression);
                }
                else
                {
                    var propertyValue = CreateValueExpression(partGroup.Single(), property.PropertyType);
                    yield return Expression.Bind(property, propertyValue);
                }
            }
        }

        private static Expression GetCollectionExpression(PropertyInfo property, IEnumerable<Parameters.ParameterComponent> parts)
        {
            var variableExpr = Expression.Variable(property.PropertyType);
            return Expression.Block(new[] { variableExpr }, GetCollectionCreationExpressions(variableExpr, property, parts));
        }

        private static IEnumerable<Expression> GetCollectionCreationExpressions(ParameterExpression variableExpr, PropertyInfo property, IEnumerable<Parameters.ParameterComponent> parts)
        {
            LabelTarget returnTarget = Expression.Label(property.PropertyType);

            GotoExpression returnExpression = Expression.Return(
                returnTarget,
                variableExpr,
                property.PropertyType);

            LabelExpression returnLabel = Expression.Label(returnTarget, Expression.New(property.PropertyType.GetConstructor(Array.Empty<Type>())));

            yield return Expression.Assign(variableExpr, Expression.New(property.PropertyType.GetConstructor(Array.Empty<Type>())));
            foreach (var part in parts)
            {
                yield return Expression.Call(variableExpr, GetMethod(variableExpr.Type, "Add"), CreateValueExpression(part, property.PropertyType));
            }

            yield return returnExpression;
            yield return returnLabel;
        }

        private static Expression MoveItem(Expression result, int source, int destination)
        {
            var propertyInfo = GetProperty(result.Type, "Item");
            var variable = Expression.Variable(propertyInfo.PropertyType, "item");
            var block = Expression.Block(
                new[] { variable },
                Expression.Assign(
                    variable,
                    Expression.MakeIndex(result, propertyInfo, new[] { Expression.Constant(source) })),
                Expression.Call(result, GetMethod(result.Type, "RemoveAt"), Expression.Constant(source)),
                Expression.Call(
                    result,
                    GetMethod(result.Type, "Insert"),
                    Expression.Constant(Math.Max(0, destination - 1)),
                    variable));
            return block;
        }

        private static Expression InsertValue(Expression result, Expression valueExpression, int insertIndex)
        {
            return result switch
            {
                MemberExpression me when me.Type.IsGenericType
                                         && GetMethod(me.Type, "Insert") != null =>
                    Expression.Block(
                        Expression.IfThen(
                            Expression.Equal(me, Expression.Default(result.Type)),
                            Expression.Throw(Expression.New(typeof(InvalidOperationException)))),
                        Expression.Call(me, GetMethod(me.Type, "Insert"), Expression.Constant(insertIndex), valueExpression)),
                _ => result,
            };
        }

        private static Expression AddValue(Expression result, Expression value)
        {
            return result switch
            {
                MemberExpression me when me.Type.IsGenericType
                                         && GetMethod(me.Type, "Add") != null =>
                    Expression.Block(
                        Expression.IfThen(
                            Expression.Equal(me, Expression.Default(result.Type)),
                            Expression.Throw(Expression.New(typeof(InvalidOperationException)))),
                        Expression.Call(me, GetMethod(me.Type, "Add"), value)),
                MemberExpression me => Expression.Block(
                    Expression.IfThen(
                        Expression.NotEqual(me, Expression.Default(result.Type)),
                        Expression.Throw(Expression.New(typeof(InvalidOperationException)))),
                    Expression.Assign(me, value)),
                _ => result,
            };
        }

        private static Expression DeleteValue(Expression result)
        {
            return result switch
            {
                IndexExpression indexExpression => Expression.Call(
                    indexExpression.Object,
                    GetMethod(indexExpression.Object!.Type, "RemoveAt"),
                    indexExpression.Arguments),
                MemberExpression me when me.Type.IsGenericType
                                         && typeof(List<>).IsAssignableFrom(me.Type.GetGenericTypeDefinition()) =>
                   Expression.Call(me, GetMethod(me.Type, "Clear")),
                MemberExpression me => Expression.Assign(me, Expression.Default(me.Type)),
                _ => result,
            };
        }

        private static MethodInfo GetMethod(Type constantType, string methodName)
        {
            var propertyInfos = constantType.GetMethods();
            var property =
                propertyInfos.FirstOrDefault(p => p.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

            return property;
        }

        private static PropertyInfo GetProperty(Type constantType, string propertyName)
        {
            var propertyInfos = constantType.GetProperties();
            var property =
                propertyInfos.FirstOrDefault(p => p.Name.Equals(propertyName + "Element", StringComparison.OrdinalIgnoreCase))
                ?? propertyInfos.FirstOrDefault(x => x.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

            return property;
        }

        private class ResourceVisitor : fhirExpression.ExpressionVisitor<Expression>
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
        }
    }
}