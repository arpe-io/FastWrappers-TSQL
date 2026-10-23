
-- Sample usage of the xp_RunFastBCP_secure stored procedure
-- EXEC dbo.xp_RunFastBCP_secure
--     @fastBCPDir = 'D:\FastBCP\latest',
--     @connectionType = 'mssql',
--     @sourceserver = 'localhost',
--     @sourceuser = 'FastUser',
--     @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
--     @sourcedatabase = 'tpch_test',
--     @sourcetable = 'nation',
--     @outputFile = 'nation.csv',
--     @outputDirectory = 'D:\temp\fastbcpoutput\nation',
--     @delimiter = '|',
--     @usequotes = 1,
--     @dateformat = 'yyyy-MM-dd HH24:mm:ss',
--     @encoding = 'utf-8',
--     @method = 'None',
--     @runid = 'test_FastBCP_export_nation'


-- EXEC dbo.xp_RunFastBCP_secure
--     @fastBCPDir = 'D:\FastBCP\latest',
--     @connectionType = 'mssql',
--     @sourceserver = 'localhost',
--     @sourceuser = 'FastUser',
--     @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
--     @sourcedatabase = 'tpch_test',
--     @query = 'SELECT top 1000 * FROM orders',
--     @outputFile = 'orders_output.csv',
--     @outputDirectory = 'D:\temp\fastbcpoutput\',
--     @delimiter = '|',
--     @usequotes = 1,
--     @dateformat = 'yyyy-MM-dd HH24:mm:ss',
--     @encoding = 'utf-8',
--     @method = 'None',
--     @runid = 'test_FastBCP_export_orders'

-- EXEC dbo.xp_RunFastBCP_secure
--     @fastBCPDir = 'D:\FastBCP\latest',
--     @connectionType = 'mssql',
--     @sourceserver = 'localhost',
--     @sourceuser = 'FastUser',
--     @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
--     @sourcedatabase = 'tpch_test',
--     @query = 'SELECT top 1000 * FROM orders',
--     @outputFile = 'orders_output.csv',
--     @outputDirectory = 'D:\temp\fastbcpoutput\orders\physical',
--     @delimiter = '|',
--     @usequotes = 1,
--     @dateformat = 'yyyy-MM-dd HH24:mm:ss',
--     @encoding = 'utf-8',
--     @method = 'Physical',
--	   @degree = 8,
--     @runid = 'test_FastBCP_export_orders_physical'

-- EXEC dbo.xp_RunFastBCP_secure
--     @fastBCPDir = 'D:\FastBCP\latest',
--     @connectionType = 'mssql',
--     @sourceserver = 'localhost',
--     @sourceuser = 'FastUser',
--     @sourcepasswordSecure = 'wi1/VHz9s+fp45186iLYYQ==',
--     @sourcedatabase = 'tpch_test',
--     @query = 'select * from (SELECT *, year(o_orderdate)*100+month(o_orderdate) as o_ordermonth FROM orders where o_orderdate >= ''19980101'') src',
--     @outputFile = 'orders_output.parquet',
--     @outputDirectory = 'D:\temp\fastbcpoutput\orders',
--     @delimiter = '|',
--     @usequotes = 1,
--     @dateformat = 'yyyy-MM-dd HH24:mm:ss',
--     @encoding = 'utf-8',
--     @method = 'DataDriven',
--     @distributeKeyColumn = 'o_ordermonth',
--     @runid = 'test_FastBCP_export_orders',
--     @debug=1




CREATE PROCEDURE [dbo].[xp_RunFastBCP_secure]
@fastBCPDir [nvarchar](1000),
@connectionType [nvarchar](30),
@sourceconnectStringSecure [nvarchar](4000) = NULL,
@sourcedsn [nvarchar](255) = NULL,
@sourceprovider [nvarchar](1000) = NULL,
@sourceserver [nvarchar](255) = NULL,
@sourceuser [nvarchar](1000) = NULL,
@sourcepasswordSecure [nvarchar](255) = NULL,
@isTrusted [bit] = 0,
@sourcedatabase [nvarchar](1000) = NULL,
@applicationintent [nvarchar](20) = 'ReadOnly',
@inputFile [nvarchar](1000) = NULL,
@query [nvarchar](4000) = NULL,
@sourceschema [nvarchar](255) = NULL,
@sourcetable [nvarchar](255) = NULL,
@outputFile [nvarchar](1000) = NULL,
@outputDirectory [nvarchar](2000) = NULL,
@delimiter [nvarchar](10) = NULL,
@usequotes [bit] = 0,
@dateformat [nvarchar](25) = NULL,
@encoding [nvarchar](50) = NULL,
@decimalSeparator [nvarchar](1) = NULL,
@parquetcompression [nvarchar](20) = 'zstd',
@degree [int] = -2,
@method [nvarchar](50) = 'None',
@distributeKeyColumn [nvarchar](1000) = NULL,
@datadrivenquery [nvarchar](4000) = NULL,
@mergeDistributedFile [bit] = 0,
@timestamped [bit] = 0,
@noheader [bit] = 0,
@boolformat [nvarchar](10) = NULL,
@runid [nvarchar](255) = NULL,
@traceparent [nvarchar](55) = NULL,
@settingsfile [nvarchar](4000) = NULL,
@config [nvarchar](4000) = NULL,
@cloudprofile [nvarchar](2000) = NULL,
@license nvarchar(4000) = NULL,
@loglevel nvarchar(50) = 'Information',
@nobanner [bit] = 0,
@debug [bit] = 0
WITH EXECUTE AS CALLER
AS
EXTERNAL NAME [FastWrappers_TSQL].[FastWrapper.FastBCPCLR].[RunFastBCP_Secure]
GO