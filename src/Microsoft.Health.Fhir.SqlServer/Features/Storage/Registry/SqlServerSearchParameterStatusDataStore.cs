﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using SqlDataReader = Microsoft.Data.SqlClient.SqlDataReader;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerSearchParameterStatusDataStore : ISearchParameterStatusDataStore
    {
        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _scopedSqlConnectionWrapperFactory;
        private readonly VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> _updateSearchParamsTvpGenerator;
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly SchemaInformation _schemaInformation;
        private readonly SqlServerSortingValidator _sortingValidator;
        private readonly ISqlServerFhirModel _fhirModel;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public SqlServerSearchParameterStatusDataStore(
            Func<IScoped<SqlConnectionWrapperFactory>> scopedSqlConnectionWrapperFactory,
            VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> updateSearchParamsTvpGenerator,
            FilebasedSearchParameterStatusDataStore.Resolver filebasedRegistry,
            SchemaInformation schemaInformation,
            SqlServerSortingValidator sortingValidator,
            ISqlServerFhirModel fhirModel,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(scopedSqlConnectionWrapperFactory, nameof(scopedSqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(updateSearchParamsTvpGenerator, nameof(updateSearchParamsTvpGenerator));
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(sortingValidator, nameof(sortingValidator));
            EnsureArg.IsNotNull(fhirModel, nameof(fhirModel));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _scopedSqlConnectionWrapperFactory = scopedSqlConnectionWrapperFactory;
            _updateSearchParamsTvpGenerator = updateSearchParamsTvpGenerator;
            _filebasedSearchParameterStatusDataStore = filebasedRegistry.Invoke();
            _schemaInformation = schemaInformation;
            _sortingValidator = sortingValidator;
            _fhirModel = fhirModel;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        // TODO: Make cancellation token an input.
        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            // If the search parameter table in SQL does not yet contain status columns
            if (_schemaInformation.Current < SchemaVersionConstants.SearchParameterStatusSchemaVersion)
            {
                // Get status information from file.
                return await _filebasedSearchParameterStatusDataStore.GetSearchParameterStatuses();
            }

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParamStatuses.PopulateCommand(sqlCommandWrapper);

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, CancellationToken.None))
                {
                    while (await sqlDataReader.ReadAsync())
                    {
                        short id;
                        string uri;
                        string stringStatus;
                        DateTimeOffset? lastUpdated;
                        bool? isPartiallySupported;

                        ResourceSearchParameterStatus resourceSearchParameterStatus;

                        if (_schemaInformation.Current >= SchemaVersionConstants.SearchParameterSynchronizationVersion)
                        {
                            (id, uri, stringStatus, lastUpdated, isPartiallySupported) = sqlDataReader.ReadRow(
                                VLatest.SearchParam.SearchParamId,
                                VLatest.SearchParam.Uri,
                                VLatest.SearchParam.Status,
                                VLatest.SearchParam.LastUpdated,
                                VLatest.SearchParam.IsPartiallySupported);

                            if (string.IsNullOrEmpty(stringStatus) || lastUpdated == null || isPartiallySupported == null)
                            {
                                // These columns are nullable because they are added to dbo.SearchParam in a later schema version.
                                // They should be populated as soon as they are added to the table and should never be null.
                                throw new SearchParameterNotSupportedException(Resources.SearchParameterStatusShouldNotBeNull);
                            }

                            var status = Enum.Parse<SearchParameterStatus>(stringStatus, true);

                            resourceSearchParameterStatus = new SqlServerResourceSearchParameterStatus
                            {
                                Id = id,
                                Uri = new Uri(uri),
                                Status = status,
                                IsPartiallySupported = (bool)isPartiallySupported,
                                LastUpdated = (DateTimeOffset)lastUpdated,
                            };
                        }
                        else
                        {
                            (uri, stringStatus, lastUpdated, isPartiallySupported) = sqlDataReader.ReadRow(
                                VLatest.SearchParam.Uri,
                                VLatest.SearchParam.Status,
                                VLatest.SearchParam.LastUpdated,
                                VLatest.SearchParam.IsPartiallySupported);

                            if (string.IsNullOrEmpty(stringStatus) || lastUpdated == null || isPartiallySupported == null)
                            {
                                // These columns are nullable because they are added to dbo.SearchParam in a later schema version.
                                // They should be populated as soon as they are added to the table and should never be null.
                                throw new SearchParameterNotSupportedException(Resources.SearchParameterStatusShouldNotBeNull);
                            }

                            var status = Enum.Parse<SearchParameterStatus>(stringStatus, true);

                            resourceSearchParameterStatus = new ResourceSearchParameterStatus
                            {
                                Uri = new Uri(uri),
                                Status = status,
                                IsPartiallySupported = (bool)isPartiallySupported,
                                LastUpdated = (DateTimeOffset)lastUpdated,
                            };
                        }

                        if (_schemaInformation.Current >= SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion)
                        {
                            // For schema versions starting from AddMinMaxForDateAndStringSearchParamVersion we will check
                            // whether the corresponding type of the search parameter is supported.
                            SearchParameterInfo paramInfo = null;
                            try
                            {
                                paramInfo = _searchParameterDefinitionManager.GetSearchParameter(resourceSearchParameterStatus.Uri);
                            }
                            catch (SearchParameterNotSupportedException)
                            {
                            }

                            if (paramInfo != null && SqlServerSortingValidator.SupportedSortParamTypes.Contains(paramInfo.Type))
                            {
                                resourceSearchParameterStatus.SortStatus = SortParameterStatus.Enabled;
                            }
                            else
                            {
                                resourceSearchParameterStatus.SortStatus = SortParameterStatus.Disabled;
                            }
                        }
                        else
                        {
                            if (_sortingValidator.SupportedParameterUris.Contains(resourceSearchParameterStatus.Uri))
                            {
                                resourceSearchParameterStatus.SortStatus = SortParameterStatus.Enabled;
                            }
                            else
                            {
                                resourceSearchParameterStatus.SortStatus = SortParameterStatus.Disabled;
                            }
                        }

                        parameterStatuses.Add(resourceSearchParameterStatus);
                    }
                }

                return parameterStatuses;
            }
        }

        // TODO: Make cancellation token an input.
        public async Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (!statuses.Any())
            {
                return;
            }

            if (_schemaInformation.Current < SchemaVersionConstants.SearchParameterStatusSchemaVersion)
            {
                throw new BadRequestException(Resources.SchemaVersionNeedsToBeUpgraded);
            }

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.UpsertSearchParams.PopulateCommand(sqlCommandWrapper, _updateSearchParamsTvpGenerator.Generate(statuses.ToList()));

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, CancellationToken.None))
                {
                    while (await sqlDataReader.ReadAsync())
                    {
                        // The upsert procedure returns the search parameters that were new.
                        (short searchParamId, string searchParamUri) = sqlDataReader.ReadRow(VLatest.SearchParam.SearchParamId, VLatest.SearchParam.Uri);

                        // Add the new search parameters to the FHIR model dictionary.
                        _fhirModel.TryAddSearchParamIdToUriMapping(searchParamUri, searchParamId);
                    }
                }
            }
        }

        // Synchronize the FHIR model dictionary with the data in SQL search parameter status table
        public void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses)
        {
            foreach (ResourceSearchParameterStatus resourceSearchParameterStatus in statuses)
            {
                var status = (SqlServerResourceSearchParameterStatus)resourceSearchParameterStatus;

                // Add the new search parameters to the FHIR model dictionary.
                _fhirModel.TryAddSearchParamIdToUriMapping(status.Uri.ToString(), status.Id);
            }
        }
    }
}
