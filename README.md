# FastWrappers-TSQL

A project to wrap FastTransfer and FastBCP in a CLR assembly so that they can be called from T-SQL through CLR stored procedures.
As a reminder :
- **FastTransfer** is a CLI that allows import from files or transfer of data between databases using streaming and parallel mechanisms for high performance
- **FastBCP** is a CLI that allows export of data from databases to files (csv, tsv, json, bson, parquet, xlsx and binary, local or cloud storage) using streaming and parallel mechanisms for high performance

## Compatibility

| FastWrappers-TSQL | FastTransfer | FastBCP | SQL Server (.bak) | SQL Server (.sql) |
|---|---|---|---|---|
| 1.1.0 | 0.18.x | 1.4.x | 2019+ | 2017+ |

## Installation

Download the latest release from the [Releases page](https://github.com/aetperf/FastWrappers-TSQL/releases). Each release provides 2 installation options:

### Installation Methods

#### 1. **FastWrappers-TSQL.bak** (Recommended) 
SQL Server Backup file - **Requires SQL Server 2019 or higher**

✅ **Fastest installation method**  
✅ **Pre-configured database with all objects**

⚠️ **Post-installation configuration required:**
- Enable CLR integration (`sp_configure 'clr enabled', 1`)
- Trust the assembly using `sp_add_trusted_assembly` (see [Post-Installation Configuration](#post-installation-configuration))

**Installation:**
```sql
-- Restore the database
RESTORE DATABASE [FastWrappers-TSQL] 
FROM DISK = 'C:\path\to\FastWrappers-TSQL.bak' 
WITH MOVE 'FastWrappers-TSQL' TO 'C:\path\to\FastWrappers-TSQL.mdf',
     MOVE 'FastWrappers-TSQL_log' TO 'C:\path\to\FastWrappers-TSQL_log.ldf';
GO
```

#### 2. **FastWrappers-TSQL.sql** (Alternative, also for older SQL Server versions)
Self-contained SQL script - **Compatible with SQL Server 2017 or higher** (`sp_add_trusted_assembly` is required)

✅ **Complete installation in a single script**  
✅ **Includes database creation, CLR activation, trusted assembly configuration, and all objects**  
✅ **No post-installation configuration needed**  
✅ **Can be run again to upgrade an existing installation**

**Installation:**
```sql
-- Simply execute the script in SSMS or using sqlcmd
sqlcmd -S YourServer -i FastWrappers-TSQL.sql
```

**What's included:**
- Database creation with proper settings (an existing `FastWrappers-TSQL` database is kept unless `@AllowDatabaseDrop = 1` is set at the top of the script)
- CLR integration enabled
- Assembly registration with `sp_add_trusted_assembly` (secure method)
- All stored procedures and functions
- Security roles (FastTransfer_Executor, FastBCP_Executor)

### Upgrading

- **With the .sql script**: run the new script on the server. The existing database is kept, the assembly, the procedures and the function are recreated, and the hash of the previous assembly is removed from `sys.trusted_assemblies`.
- **With the .bak file**: restore the new backup with `REPLACE`, then remove the previous hash and trust the new one (see the note in [Option 1](#option-1-using-sp_add_trusted_assembly-recommended-for-production)).

⚠️ **Upgrading from 1.0.x installed with the .sql script**: the FastBCP parameters are now the same as in the `.bak` and in this README. Rename `@fastBcpDir` → `@fastBCPDir`, `@sourceConnectStringEnc` → `@sourceconnectStringSecure`, `@sourcepasswordEnc` → `@sourcepasswordSecure` and `@trusted` → `@isTrusted` in your calls. The FastBCP formatting parameters (`@delimiter`, `@dateformat`, `@encoding`, `@decimalSeparator`, `@boolformat`) now default to `NULL`, so FastBCP's own defaults apply.

### Post-Installation Configuration

After restoring the database (especially from .bak), you **must** configure CLR integration. Two options are available depending on your environment:

#### Option 1: Using sp_add_trusted_assembly (Recommended for Production) 

This approach is **more secure** as it keeps TRUSTWORTHY OFF and doesn't require changing the database owner:

```sql
-- Enable CLR integration
EXEC sp_configure 'show advanced options', 1;
RECONFIGURE;
GO

EXEC sp_configure 'clr enabled', 1;
RECONFIGURE;
GO

-- Extract the assembly hash and add it to trusted assemblies
USE [FastWrappers-TSQL];
GO

DECLARE @hash VARBINARY(64);

SELECT @hash = HASHBYTES('SHA2_512', content)
FROM sys.assembly_files
WHERE assembly_id = (
    SELECT assembly_id 
    FROM sys.assemblies 
    WHERE name = 'FastWrappers_TSQL'
);

IF NOT EXISTS (SELECT 1 FROM sys.trusted_assemblies WHERE [hash] = @hash)
    EXEC sys.sp_add_trusted_assembly 
        @hash = @hash,
        @description = N'FastWrappers_TSQL Assembly v1.1.0';
GO
```

**Advantages:**
- ✅ TRUSTWORTHY remains OFF (more secure)
- ✅ No need to change database owner
- ✅ Only this specific assembly is trusted

**Note:** The assembly hash changes with each release. When upgrading, remove the old hash and add the new one:
```sql
-- List the trusted hashes
SELECT [hash], [description], create_date FROM sys.trusted_assemblies;
-- Remove old version
EXEC sys.sp_drop_trusted_assembly @hash = <old_hash>;
-- Then run the sp_add_trusted_assembly script above
```

#### Option 2: Using TRUSTWORTHY ON (Quick Setup for Dev/Test)

This approach is simpler but **less secure**. Use only for development/testing environments:

```sql
-- Enable TRUSTWORTHY for signed UNSAFE assemblies
ALTER DATABASE [FastWrappers-TSQL] SET TRUSTWORTHY ON;
GO

-- Enable CLR integration
EXEC sp_configure 'show advanced options', 1;
RECONFIGURE;
GO

EXEC sp_configure 'clr enabled', 1;
RECONFIGURE;
GO

-- Set database owner to 'sa' (required for signed UNSAFE assemblies)
USE [FastWrappers-TSQL];
GO
EXEC sp_changedbowner 'sa';
GO
```

**Important:** With this method, the `sp_changedbowner 'sa'` command is **critical**. Without it, you will encounter error 0x80FC80F1 when trying to execute the stored procedures.

### Binaries and permissions

- FastTransfer and FastBCP are **not** included: install them (with a valid license or trial) on the SQL Server machine.
- `@fastTransferDir` / `@fastBCPDir` can be the directory containing the binary (e.g. `D:\FastTransfer` or `D:\FastBCP\`) or the full path of the executable (e.g. `D:\FastBCP\FastBCP.exe`).
- The processes run under the **SQL Server service account** (e.g. `NT Service\MSSQLSERVER`). This account needs read/execute permission on the binaries and the license file, and write permission on the FastBCP output directories. With `@isSourceTrusted`, `@isTargetTrusted` or `@isTrusted`, the connections are made with this account.

## Security Roles

The FastWrappers-TSQL database includes two predefined database roles to manage access to the stored procedures:

### 1. **FastTransfer_Executor**

This role grants `EXECUTE` permission on the **xp_RunFastTransfer_secure** stored procedure.

**Usage:**
```sql
-- Add a user to the FastTransfer_Executor role
ALTER ROLE [FastTransfer_Executor] ADD MEMBER [YourUserName];
GO
```

**Purpose:** Allows users to perform data transfers between databases without granting them broader permissions.

### 2. **FastBCP_Executor**

This role grants `EXECUTE` permission on the **xp_RunFastBCP_secure** stored procedure.

**Usage:**
```sql
-- Add a user to the FastBCP_Executor role
ALTER ROLE [FastBCP_Executor] ADD MEMBER [YourUserName];
GO
```

**Purpose:** Allows users to export data to files without granting them broader permissions.

### Combined Access

To grant a user access to both FastTransfer and FastBCP:

```sql
-- Add user to both roles
ALTER ROLE [FastTransfer_Executor] ADD MEMBER [YourUserName];
ALTER ROLE [FastBCP_Executor] ADD MEMBER [YourUserName];
GO
```

**Note:** Users also need access to the **dbo.EncryptString** function to generate encrypted passwords. The `.sql` script grants it to both roles; with the `.bak`, grant it explicitly:

```sql
GRANT EXECUTE ON dbo.EncryptString TO [FastTransfer_Executor];
GRANT EXECUTE ON dbo.EncryptString TO [FastBCP_Executor];
GO
```

## Available Stored Procedures

Once installed and configured, the FastWrappers-TSQL assembly provides the following objects:

### 1. **dbo.EncryptString** - Password Encryption Function
```sql
SELECT dbo.EncryptString('YourPassword')
```
Encrypts a string using AES-256. Use this function to generate the values of the `...Secure` parameters: passwords (`@sourcePasswordSecure`, `@targetPasswordSecure`, `@sourcepasswordSecure`) and connection strings (`@sourceConnectStringSecure`, `@targetConnectStringSecure`, `@sourceconnectStringSecure`).

**Returns:** Base64-encoded encrypted string

⚠️ The AES key is hard coded in `KeyProvider.cs` as a sample. Change it and compile your own assembly for production use.

### 2. **dbo.xp_RunFastTransfer_secure** - Data Transfer Wrapper
```sql
EXEC dbo.xp_RunFastTransfer_secure @fastTransferDir = '...', ...
```
Wraps the **FastTransfer** CLI to transfer data between databases with streaming and parallel processing for high performance.

**Key Features:**
- Supports 13 source connection types (clickhouse, duckdb, duckdbstream, hana, mssql, mysql, nzsql, odbc, oledb, oraodp, pgcopy, pgsql, teradata)
- Supports 10 target connection types with bulk loading (clickhousebulk, duckdb, hanabulk, msbulk, mysqlbulk, nzbulk, orabulk, oradirect, pgcopy, teradata)
- Parallel methods: None, Random, DataDriven, DBPartition, RangeId, Ntile, Timepartition, Ctid (PostgreSQL), Physloc (SQL Server), Rowid (Oracle), NZDataSlice (Netezza)
- Automatic column mapping by position or name
- Encrypted connection strings and passwords using AES-256
- Configurable batch sizes and parallelism degree
- Load modes: Append, Truncate
- Work tables support for staging data
- Custom data-driven distribution queries

**Parameters:**

| Parameter | Default | Description |
|---|---|---|
| `@fastTransferDir` | *required* | Directory of the FastTransfer binary, or full path of the executable |
| `@sourceConnectionType` | *required* | clickhouse, duckdb, duckdbstream, hana, mssql, mysql, nzsql, odbc, oledb, oraodp, pgcopy, pgsql, teradata |
| `@sourceConnectStringSecure` | NULL | Encrypted source connection string. Exclusive with server/DSN/credentials/database |
| `@sourceServer` | *required* | `Host`, `Host:Port`, `Host\Instance` or `Host:Port/TNSService`. Pass `NULL` with a connection string or a DSN |
| `@sourceDSN` | NULL | ODBC DSN (instead of `@sourceServer`) |
| `@sourceProvider` | NULL | OLEDB provider (e.g. `MSOLEDBSQL`, `NZOLEDB`) |
| `@isSourceTrusted` | 0 | Integrated authentication on the source |
| `@sourceUser` / `@sourcePasswordSecure` | NULL | Source credentials (encrypted password) |
| `@sourceDatabase` | *required* | Source database. Pass `NULL` with a connection string |
| `@fileInput` / `@query` | NULL | File containing the SQL query, or SQL query (mutually exclusive) |
| `@sourceSchema` / `@sourceTable` | NULL | Source table (required by some parallel methods) |
| `@targetConnectionType` | *required* | clickhousebulk, duckdb, hanabulk, msbulk, mysqlbulk, nzbulk, orabulk, oradirect, pgcopy, teradata |
| `@targetConnectStringSecure` | NULL | Encrypted target connection string. Exclusive with server/credentials/database |
| `@targetServer` / `@targetDatabase` | *required* | Target server and database. Pass `NULL` with a connection string |
| `@isTargetTrusted` | 0 | Integrated authentication on the target |
| `@targetUser` / `@targetPasswordSecure` | NULL | Target credentials (encrypted password) |
| `@targetSchema` / `@targetTable` | *required* | Target table |
| `@loadMode` | *required* | `Append` or `Truncate` (case sensitive) |
| `@batchSize` | 1048576 | Bulk copy batch size |
| `@useWorkTables` | 0 | Load through intermediate work tables (useful with a CCI target) |
| `@method` | `None` | Parallel method (case sensitive, see above) |
| `@distributeKeyColumn` | NULL | Required for Random, DataDriven, RangeId, Ntile and Timepartition. For Timepartition: `(datecolumn,year)`, `(datecolumn,year,month)`, `(datecolumn,year,month,day)` or `(datecolumn,year,week)` |
| `@dataDrivenQuery` | NULL | Query returning the distinct values for DataDriven |
| `@degree` | 4 | Degree of parallelism (0 = auto). Required when `@method` is not `None` |
| `@mapmethod` | `Position` | `Position` or `Name` |
| `@runId` | NULL | Run identifier for logging (GUID generated if NULL) |
| `@settingsFile` | NULL | Custom FastTransfer settings file (logging) |
| `@debug` | 0 | Print the command line and the FastTransfer output |
| `@noBanner` | 0 | Disable the FastTransfer banner |
| `@license` | NULL | Path or URL of the license file (default: `FastTransfer.lic` next to the binary) |
| `@loglevel` | `information` | `error`, `warning`, `information` or `debug` |

### 3. **dbo.xp_RunFastBCP_secure** - Data Export Wrapper
```sql
EXEC dbo.xp_RunFastBCP_secure @fastBCPDir = '...', ...
```
Wraps the **FastBCP** CLI to export data from databases to files with streaming and parallel processing for high performance.

**Key Features:**
- Supports 19 connection types (clickhouse, hana, msoledbsql, mssql, mysql, nzcopy, nzoledb, nzsql, odbc, oledb, oraodp, pgcopy, pgsql, teradata) including the ADBC drivers (adbc_mssql, adbc_pgsql, adbc_oracle, adbc_netezza, adbc_teradata)
- Multiple output formats: CSV, TSV, JSON, Parquet, BSON, Binary (PostgreSQL COPY), XLSX (Excel), with on-the-fly gzip for csv.gz, tsv.gz and json.gz
- Parquet compression codecs: Zstd (default), Snappy, Gzip, Lz4, Lz4Raw, None
- Parallel methods: None, Random, DataDriven, DBPartition, RangeId, Ntile, Timepartition, Physical (Ctid, Physloc, Rowid and NZDataSlice are deprecated in FastBCP and replaced by Physical; they still work with a warning)
- Cloud storage support: AWS S3, Azure Blob Storage, Azure Data Lake Gen2, Google Cloud Storage, S3-Compatible, OneLake
- Configurable CSV/TSV formatting (delimiter, quotes, date format, decimal separator, boolean format, encoding)
- Encrypted connection strings and passwords using AES-256
- File merge option for parallel exports
- Timestamped output files
- YAML configuration file support (`@config`)
- OpenTelemetry trace propagation (`@traceparent`)

**Parameters:**

| Parameter | Default | Description |
|---|---|---|
| `@fastBCPDir` | *required* | Directory of the FastBCP binary, or full path of the executable |
| `@connectionType` | *required* | See the connection types above |
| `@sourceconnectStringSecure` | NULL | Encrypted connection string. Exclusive with DSN/provider/server/credentials/trusted/database |
| `@sourcedsn` | NULL | ODBC DSN (exclusive with provider/server) |
| `@sourceprovider` | NULL | OLEDB provider (e.g. `MSOLEDBSQL`, `NZOLEDB`) |
| `@sourceserver` | NULL | `Server`, `Server\Instance` or `Server:Port` |
| `@sourceuser` / `@sourcepasswordSecure` | NULL | Credentials (encrypted password) |
| `@isTrusted` | 0 | Integrated authentication (exclusive with user/password) |
| `@sourcedatabase` | NULL | Required when no connection string is used |
| `@applicationintent` | `ReadOnly` | `ReadOnly` or `ReadWrite` (mssql, msoledbsql and oledb only) |
| `@inputFile` / `@query` | NULL | File containing the SQL query, or SQL query (mutually exclusive) |
| `@sourceschema` / `@sourcetable` | NULL | Source table |
| `@outputFile` | NULL | Output file name. The extension sets the format: `.csv`, `.tsv`, `.json`, `.bson`, `.parquet`, `.xlsx`, binary, and `.csv.gz`, `.tsv.gz`, `.json.gz` |
| `@outputDirectory` | NULL | Output directory (local or cloud). Supports `{sourcedatabase}`, `{sourceschema}` and `{sourcetable}` placeholders |
| `@delimiter` | NULL (FastBCP: `\|`) | Field delimiter |
| `@usequotes` | 0 | Quote string fields |
| `@dateformat` | NULL (FastBCP: `yyyy-MM-dd HH:mm:sszzz`) | .NET date format, e.g. `yyyy-MM-dd HH:mm:ss` |
| `@encoding` | NULL (FastBCP: `UTF-8`) | e.g. `UTF-8`, `windows-1252` |
| `@decimalSeparator` | NULL (FastBCP: `,`) | `.` or `,` |
| `@parquetcompression` | `zstd` | None, Snappy, Gzip, Lz4, Lz4Raw, Zstd (case insensitive) |
| `@degree` | -2 | 0 = all cores, negative = cores / abs(degree), or a fixed degree |
| `@method` | `None` | Parallel method (case insensitive, see above) |
| `@distributeKeyColumn` | NULL | Distribution column. For Timepartition: `(datecolumn,year)`, `(datecolumn,year,month)` or `(datecolumn,year,month,day)` |
| `@datadrivenquery` | NULL | Query returning the distinct values for DataDriven |
| `@mergeDistributedFile` | 0 | Merge the files produced by a parallel export into one file |
| `@timestamped` | 0 | Add a `_yyyyMMddHHmm` timestamp to the file name |
| `@noheader` | 0 | No header line (CSV only) |
| `@boolformat` | NULL (FastBCP: `automatic`) | Boolean output format |
| `@runid` | NULL | Run identifier for logging |
| `@traceparent` | NULL | W3C traceparent for OpenTelemetry, e.g. `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01` |
| `@settingsfile` | NULL | Custom FastBCP settings file (logging) |
| `@config` | NULL | YAML config file. Parameters passed to the procedure override the config values |
| `@cloudprofile` | NULL | Cloud provider profile when `@outputDirectory` is on object storage |
| `@license` | NULL | Path or URL of the license file (default: `FastBCP.lic` next to the binary) |
| `@loglevel` | `Information` | `Information` or `Debug` |
| `@nobanner` | 0 | Disable the FastBCP banner |
| `@debug` | 0 | Print the command line and the FastBCP output |

**Note:** `@method`, `@degree`, `@mergeDistributedFile`, `@parquetcompression`, `@applicationintent` and `@loglevel` have a default value and are always passed to FastBCP, so they override the same values in a `@config` file. Pass them explicitly to the value you want.

## Logging and Output

### FastTransfer Output

**xp_RunFastTransfer_secure** returns a result set with the transfer metrics:

| Column | Type | Description |
|--------|------|-------------|
| targetdatabase | nvarchar(128) | Target database name |
| targetSchema | nvarchar(128) | Target schema name |
| targetTable | nvarchar(128) | Target table name |
| TotalRows | bigint | Number of rows transferred |
| TotalColumns | int | Number of columns transferred |
| TotalCells | bigint | Total cells transferred (rows × columns) |
| TotalTimeMs | bigint | Total execution time in milliseconds |
| Status | int | Exit code (0 = success, non-zero = error) |
| StdErr | nvarchar(max) | Error message and end of the FastTransfer output if Status ≠ 0 |

**Example output:**
```
targetdatabase  targetSchema  targetTable  TotalRows  TotalColumns  TotalCells   TotalTimeMs  Status  StdErr
postgres        public        orders       15000000   9             135000000    27502        0       
```

### FastBCP Output

**xp_RunFastBCP_secure** returns a result set with the export metrics:

| Column | Type | Description |
|--------|------|-------------|
| sourcedatabase | nvarchar(256) | Source database name |
| source | nvarchar(max) | Query, input file or `database.schema.table` |
| TotalRows | bigint | Number of rows exported |
| TotalColumns | int | Number of columns exported |
| TotalCells | bigint | Total cells exported (rows × columns) |
| TotalTimeMs | bigint | Total execution time in milliseconds |
| Status | int | Exit code (0 = success, non-zero = error) |
| Directory | nvarchar(max) | Output directory |
| Files | nvarchar(max) | Files created (comma separated). The merged file when `@mergeDistributedFile = 1` |
| StdOut | nvarchar(max) | End of the FastBCP output (only with `@debug = 1`) |
| StdErr | nvarchar(max) | Error message and end of the FastBCP output if Status ≠ 0 |

When the procedure fails (Status ≠ 0), the result set is returned and then an error is raised, so that a calling job or `TRY...CATCH` sees the failure.

### Debug Mode (@debug = 1)

When you set `@debug = 1`, both stored procedures also output in the Messages tab:

1. **The complete command line** being executed:
   ```
   FastTransfer Command D:\FastTransfer\FastTransfer.exe --sourceconnectiontype "mssql" --sourceserver "localhost" --sourceuser "FastUser" --sourcepassword "<hidden>" ...
   ```
   *(Passwords and connection strings are masked with `<hidden>`)*

2. **The full console output (stdout)** of FastTransfer or FastBCP: progress, detailed logs, metrics, warnings.

## Usage Examples

All the examples use encrypted passwords: use `SELECT dbo.EncryptString('<YourPassWordToEncrypt>')` to get them.

### Copy one table using 12 threads between two MSSQL instances 
```TSQL
EXEC dbo.xp_RunFastTransfer_secure
     @fastTransferDir = 'C:\FastTransfer\win-x64\latest\',
     @sourceConnectionType = N'mssql',
     @sourceServer = N'localhost',
     @sourceUser = N'FastUser',
     @sourcePasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
     @sourceDatabase = N'tpch_test',
     @sourceSchema = N'dbo',
     @sourceTable = N'orders',
     @targetConnectionType = N'msbulk',
     @targetServer = N'localhost\SS2025',
     @targetUser = N'FastUser',
     @targetPasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
     @targetDatabase = N'tpch_test',
     @targetSchema = N'dbo',
     @targetTable = N'orders_3',
     @loadMode = N'Truncate',
     @batchSize = 130000,
     @method = N'RangeId',
     @distributeKeyColumn = N'o_orderkey',
     @degree = 12,
     @mapmethod = 'Name',
     @runId = N'CLRWrap_Run_MS2MS_20250328'
```

### Copy one table from Oracle to SQL Server using 12 threads 
```TSQL
EXEC dbo.xp_RunFastTransfer_secure
     @fastTransferDir = 'C:\FastTransfer\win-x64\latest\',
     @sourceConnectionType = N'oraodp',
     @sourceServer = N'oraserver:1521/ORCLPDB1',
     @sourceUser = N'TPCH',
     @sourcePasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
     @sourceDatabase = N'ORCLPDB1',
     @sourceSchema = N'TPCH',
     @sourceTable = N'ORDERS',
     @targetConnectionType = N'msbulk',
     @targetServer = N'localhost',
     @isTargetTrusted = 1,
     @targetDatabase = N'tpch_test',
     @targetSchema = N'dbo',
     @targetTable = N'orders',
     @loadMode = N'Truncate',
     @method = N'Rowid',
     @degree = 12,
     @mapmethod = 'Name',
     @runId = N'CLRWrap_Run_ORA2MS_20250328'
```

### Copy one table by month using 8 threads (Timepartition) 
```TSQL
EXEC dbo.xp_RunFastTransfer_secure
     @fastTransferDir = 'D:\FastTransfer',
     @sourceConnectionType = N'mssql',
     @sourceServer = N'localhost',
     @isSourceTrusted = 1,
     @sourceDatabase = N'AdventureWorks2022',
     @sourceSchema = N'Sales',
     @sourceTable = N'SalesOrderHeader',
     @targetConnectionType = N'msbulk',
     @targetServer = N'localhost',
     @isTargetTrusted = 1,
     @targetDatabase = N'staging',
     @targetSchema = N'dbo',
     @targetTable = N'SalesOrderHeader',
     @loadMode = N'Truncate',
     @method = N'Timepartition',
     @distributeKeyColumn = N'(OrderDate,year,month)',
     @degree = 8
```

### Copy a query result using an encrypted connection string 
```TSQL
DECLARE @cs nvarchar(4000) = dbo.EncryptString('Server=localhost;Database=tpch_test;Integrated Security=True;TrustServerCertificate=True');

EXEC dbo.xp_RunFastTransfer_secure
     @fastTransferDir = 'D:\FastTransfer',
     @sourceConnectionType = N'mssql',
     @sourceConnectStringSecure = @cs,
     @sourceServer = NULL,
     @sourceDatabase = NULL,
     @query = N'SELECT * FROM dbo.orders WHERE o_orderdate >= ''19980101''',
     @targetConnectionType = N'pgcopy',
     @targetServer = N'pgserver:5432',
     @targetUser = N'postgres',
     @targetPasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
     @targetDatabase = N'tpch',
     @targetSchema = N'public',
     @targetTable = N'orders',
     @loadMode = N'Append'
```

### Export one table to a csv file mono thread 
```TSQL
EXEC dbo.xp_RunFastBCP_secure
   @fastBCPDir = 'D:\FastBCP\latest',
   @connectionType = 'mssql',
   @sourceserver = 'localhost',
   @sourceuser = 'FastUser',
   @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
   @sourcedatabase = 'tpch',
   @sourceschema = 'dbo',
   @sourcetable = 'orders',
   @outputFile = 'orders_output.csv',
   @outputDirectory = 'D:\temp\fastbcpoutput\{sourcedatabase}\{sourceschema}\{sourcetable}',
   @delimiter = '|',
   @usequotes = 1,
   @dateformat = 'yyyy-MM-dd HH:mm:ss',
   @encoding = 'utf-8',
   @method = 'None',
   @runid = 'test_FastBCP_export_orders'
```

### Export one table to 8 parquet files using 8 threads 
```TSQL
EXEC dbo.xp_RunFastBCP_secure
   @fastBCPDir = 'D:\FastBCP\latest',
   @connectionType = 'mssql',
   @sourceserver = 'localhost',
   @sourceuser = 'FastUser',
   @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
   @sourcedatabase = 'tpch10_collation_bin2',
   @sourceschema = 'dbo',
   @sourcetable = 'orders_15M',
   @outputFile = 'orders_output.parquet',
   @outputDirectory = 'D:\temp\fastbcpoutput\{sourcedatabase}\{sourceschema}\{sourcetable}',
   @parquetcompression = 'Zstd',
   @method = 'Physical',
   @degree = 8,
   @mergeDistributedFile = 0
```

### Export one table to one csv file per month using 8 threads 
```TSQL
EXEC dbo.xp_RunFastBCP_secure
   @fastBCPDir = 'D:\FastBCP\latest',
   @connectionType = 'mssql',
   @sourceserver = 'localhost',
   @sourceuser = 'FastUser',
   @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
   @sourcedatabase = 'tpch10_collation_bin2',
   @query = 'SELECT * FROM (SELECT *, year(o_orderdate)*100+month(o_orderdate) o_ordermonth FROM orders_15M) src',
   @outputFile = 'orders_output.csv',
   @outputDirectory = 'D:\temp\fastbcpoutput\{sourcedatabase}\orders_15M',
   @method = 'DataDriven',
   @distributeKeyColumn = 'o_ordermonth',
   @degree = 8,
   @mergeDistributedFile = 0
```

The same export with the Timepartition method (one sub-directory per year, e.g. `year=2024`), without building the month column in a query:
```TSQL
EXEC dbo.xp_RunFastBCP_secure
   @fastBCPDir = 'D:\FastBCP\latest',
   @connectionType = 'mssql',
   @sourceserver = 'localhost',
   @isTrusted = 1,
   @sourcedatabase = 'tpch10_collation_bin2',
   @sourceschema = 'dbo',
   @sourcetable = 'orders_15M',
   @outputFile = 'orders_output.csv',
   @outputDirectory = 'D:\temp\fastbcpoutput\{sourcedatabase}\{sourceschema}\{sourcetable}',
   @method = 'Timepartition',
   @distributeKeyColumn = '(o_orderdate,year)',
   @degree = 8
```

### Export a table to a gzip json file with the ADBC driver and trace propagation 
```TSQL
EXEC dbo.xp_RunFastBCP_secure
   @fastBCPDir = 'D:\FastBCP\latest',
   @connectionType = 'adbc_mssql',
   @sourceserver = 'localhost:1433',
   @sourceuser = 'FastUser',
   @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
   @sourcedatabase = 'tpch',
   @sourceschema = 'dbo',
   @sourcetable = 'orders',
   @outputFile = 'orders.json.gz',
   @outputDirectory = 'D:\temp\fastbcpoutput\orders',
   @traceparent = '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
   @runid = 'orders_json_export'
```
*(Use SQL authentication with the ADBC drivers: integrated authentication was refused by `adbc_mssql` in our tests)*

More examples in [samples/samples.sql](samples/samples.sql).
