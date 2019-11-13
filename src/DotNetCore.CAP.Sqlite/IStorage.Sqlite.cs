// Copyright (c) .NET Core Community. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DotNetCore.CAP.Dashboard;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetCore.CAP.Sqlite
{
    public class SqliteStorage : IStorage
    {
        private readonly IOptions<CapOptions> _capOptions;
        private readonly IOptions<SqliteOptions> _options;
        private readonly IDbConnection _existingConnection = null;
        private readonly ILogger _logger;

        public SqliteStorage(
            ILogger<SqliteStorage> logger,
            IOptions<SqliteOptions> options, 
            IOptions<CapOptions> capOptions)
        {
            _options = options;
            _capOptions = capOptions;
            _logger = logger;
        }

        public IStorageConnection GetConnection()
        {
            return new SqliteStorageConnection(_options, _capOptions);
        }

        public IMonitoringApi GetMonitoringApi()
        {
            return new SqliteMonitoringApi(this, _options);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            SQLitePCL.Batteries.Init();
            var sql = CreateDbTablesScript(_options.Value.TableNamePrefix);
            using (var connection = new SqliteConnection(_options.Value.ConnectionString))
            {
                await connection.ExecuteAsync(sql);
            }

            _logger.LogDebug("Ensuring all create database tables script are applied.");
        }

        protected virtual string CreateDbTablesScript(string prefix)
        {
            var batchSql =
                $@"
CREATE TABLE IF NOT EXISTS `{prefix}.received` (
  `Id` bigint NOT NULL,
  `Version` varchar(20) DEFAULT NULL,
  `Name` varchar(400) NOT NULL,
  `Group` varchar(200) DEFAULT NULL,
  `Content` longtext,
  `Retries` int(11) DEFAULT NULL,
  `Added` datetime NOT NULL,
  `ExpiresAt` datetime DEFAULT NULL,
  `StatusName` varchar(50) NOT NULL,
  PRIMARY KEY (`Id`)
); 

CREATE TABLE IF NOT EXISTS `{prefix}.published` (
  `Id` bigint NOT NULL,
  `Version` varchar(20) DEFAULT NULL,
  `Name` varchar(200) NOT NULL,
  `Content` longtext,
  `Retries` int(11) DEFAULT NULL,
  `Added` datetime NOT NULL,
  `ExpiresAt` datetime DEFAULT NULL,
  `StatusName` varchar(40) NOT NULL,
  PRIMARY KEY (`Id`)
)";
            return batchSql;
        }

        internal T UseConnection<T>(Func<IDbConnection, T> func)
        {
            IDbConnection connection = null;

            try
            {
                connection = CreateAndOpenConnection();
                return func(connection);
            }
            finally
            {
                ReleaseConnection(connection);
            }
        }

        internal IDbConnection CreateAndOpenConnection()
        {
            var connection = _existingConnection ?? new SqliteConnection(_options.Value.ConnectionString);

            if (connection.State == ConnectionState.Closed)
            {
                connection.Open();
            }

            return connection;
        }

        internal bool IsExistingConnection(IDbConnection connection)
        {
            return connection != null && ReferenceEquals(connection, _existingConnection);
        }

        internal void ReleaseConnection(IDbConnection connection)
        {
            if (connection != null && !IsExistingConnection(connection))
            {
                connection.Dispose();
            }
        }
    }
}