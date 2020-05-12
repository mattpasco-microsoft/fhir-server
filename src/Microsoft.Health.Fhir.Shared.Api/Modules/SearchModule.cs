﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of search components.
    /// </summary>
    public class SearchModule : IStartupModule
    {
        private readonly FhirServerConfiguration _configuration;

        public SearchModule(FhirServerConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddSingleton<IUrlResolver, UrlResolver>();
            services.AddSingleton<IBundleFactory, BundleFactory>();

            services.AddSingleton<IReferenceSearchValueParser, ReferenceSearchValueParser>();

            services.Add<SearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<ISearchParameterDefinitionManager>()
                .AsService<IStartable>();

            services.Add<SearchableSearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsDelegate<ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver>();

            services.Add<SupportedSearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsDelegate<ISearchParameterDefinitionManager.SupportedSearchParameterDefinitionManagerResolver>();

            services.Add<SearchParameterStatusManager>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<FilebasedSearchParameterRegistry>()
                .Singleton()
                .AsSelf()
                .AsService<ISearchParameterRegistry>()
                .AsDelegate<FilebasedSearchParameterRegistry.Resolver>();

            services.Add<SearchParameterSupportResolver>()
                .Singleton()
                .AsImplementedInterfaces();

            // Existing model-based converters
            services.TypesInSameAssemblyAs<IFhirElementToSearchValueTypeConverter>()
                .AssignableTo<IFhirElementToSearchValueTypeConverter>()
                .Singleton()
                .AsSelf()
                .AsService<IFhirElementToSearchValueTypeConverter>();

            services.Add<FhirElementToSearchValueTypeConverterManager>()
                .Singleton()
                .AsSelf()
                .AsService<IFhirElementToSearchValueTypeConverterManager>();

            if (_configuration.CoreFeatures.UseTypedElementIndexer)
            {
                // TypedElement based converters
                services.TypesInSameAssemblyAs<IFhirNodeToSearchValueTypeConverter>()
                    .AssignableTo<IFhirNodeToSearchValueTypeConverter>()
                    .Singleton()
                    .AsService<IFhirNodeToSearchValueTypeConverter>();

                services.Add<FhirNodeToSearchValueTypeConverterManager>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IFhirNodeToSearchValueTypeConverterManager>();

                services.AddSingleton<ISearchIndexer, TypedElementSearchIndexer>();

                services.Add<CodeSystemResolver>()
                    .Singleton()
                    .AsSelf()
                    .AsImplementedInterfaces();
            }
            else
            {
                services.AddSingleton<ISearchIndexer, SearchIndexer>();
            }

            services.AddSingleton<ISearchParameterExpressionParser, SearchParameterExpressionParser>();
            services.AddSingleton<IExpressionParser, ExpressionParser>();
            services.AddSingleton<ISearchOptionsFactory, SearchOptionsFactory>();
            services.AddSingleton<IReferenceToElementResolver, LightweightReferenceToElementResolver>();

            services.Add<CompartmentDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<IStartable>()
                .AsService<ICompartmentDefinitionManager>();

            services.Add<CompartmentIndexer>()
                .Singleton()
                .AsSelf()
                .AsService<ICompartmentIndexer>();
        }
    }
}
