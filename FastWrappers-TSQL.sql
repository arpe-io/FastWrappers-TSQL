-- =====================================================================
-- FastWrappers-TSQL - Complete Installation Script
-- =====================================================================
-- This script creates the FastWrappers-TSQL database with all
-- required CLR assemblies, stored procedures, functions, and security roles.
-- =====================================================================
-- Prerequisites:
-- 1. SQL Server 2016 or later
-- 2. CLR integration enabled (see instructions below)
-- 3. FastTransfer and FastBCP binaries available
-- =====================================================================

-- =====================================================================
-- STEP 1: Enable CLR Integration (if not already enabled)
-- =====================================================================
EXEC sp_configure 'show advanced options', 1;
RECONFIGURE;
GO

EXEC sp_configure 'clr enabled', 1;
RECONFIGURE;
GO

-- =====================================================================
-- STEP 2: Create Database
-- =====================================================================
USE [master]
GO

DECLARE @AllowDatabaseDrop BIT = 0; -- Set to 1 to allow dropping existing FastWrappers-TSQL database

IF EXISTS (SELECT name FROM sys.databases WHERE name = 'FastWrappers-TSQL')
BEGIN
    IF @AllowDatabaseDrop = 1
    BEGIN
        PRINT 'Database FastWrappers-TSQL already exists. Dropping it because @AllowDatabaseDrop = 1...';
        ALTER DATABASE [FastWrappers-TSQL] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
        DROP DATABASE [FastWrappers-TSQL];
        PRINT 'Database FastWrappers-TSQL dropped successfully.';
    END
    ELSE
    BEGIN
        PRINT 'Database FastWrappers-TSQL already exists. Skipping creation because @AllowDatabaseDrop = 0.';
        PRINT 'Existing database will be used. Objects will be recreated/updated.';
    END
END
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'FastWrappers-TSQL')
BEGIN
    CREATE DATABASE [FastWrappers-TSQL]
    
    ALTER DATABASE [FastWrappers-TSQL] SET RECOVERY SIMPLE;
    ALTER DATABASE [FastWrappers-TSQL] SET PAGE_VERIFY CHECKSUM;
    
    PRINT 'Database FastWrappers-TSQL created successfully.';
END
GO

-- =====================================================================
-- STEP 3: Configure Database for CLR with sp_add_trusted_assembly
-- =====================================================================
-- This method uses sp_add_trusted_assembly and is more secure
-- The assembly hex will be injected during the build process

USE [FastWrappers-TSQL];
GO

-- Drop existing assembly if it exists
IF EXISTS (SELECT * FROM sys.assemblies WHERE name = 'FastWrappers_TSQL')
BEGIN
    -- Drop dependent objects first
    IF EXISTS (SELECT * FROM sys.objects WHERE name = 'xp_RunFastBCP_secure' AND type = 'PC')
        DROP PROCEDURE [dbo].[xp_RunFastBCP_secure];
    
    IF EXISTS (SELECT * FROM sys.objects WHERE name = 'xp_RunFastTransfer_secure' AND type = 'PC')
        DROP PROCEDURE [dbo].[xp_RunFastTransfer_secure];
    
    IF EXISTS (SELECT * FROM sys.objects WHERE name = 'EncryptString' AND type IN ('FN', 'TF', 'IF', 'FS'))
        DROP FUNCTION [dbo].[EncryptString];
    
    -- Remove the previous version's hash from the trusted list (avoid accumulating stale hashes)
    DECLARE @oldHash VARBINARY(64);
    SELECT @oldHash = HASHBYTES('SHA2_512', af.content)
    FROM sys.assembly_files af
    JOIN sys.assemblies a ON a.assembly_id = af.assembly_id
    WHERE a.name = 'FastWrappers_TSQL' AND af.file_id = 1;

    IF @oldHash IS NOT NULL AND EXISTS (SELECT * FROM sys.trusted_assemblies WHERE [hash] = @oldHash)
    BEGIN
        EXEC sys.sp_drop_trusted_assembly @hash = @oldHash;
        PRINT 'Previous trusted assembly hash removed.';
    END

    -- Now drop the assembly
    DROP ASSEMBLY [FastWrappers_TSQL];
    PRINT 'Existing assembly [FastWrappers_TSQL] dropped.';
END
GO

-- Add assembly to trusted list BEFORE creating it
DECLARE @assemblyBinary VARBINARY(MAX) = __ASSEMBLY_FROM_0X__;
DECLARE @hash VARBINARY(64) = HASHBYTES('SHA2_512', @assemblyBinary);

-- Remove from trusted list if already exists
IF EXISTS (SELECT * FROM sys.trusted_assemblies WHERE [hash] = @hash)
BEGIN
    EXEC sys.sp_drop_trusted_assembly @hash = @hash;
    PRINT 'Existing trusted assembly hash removed.';
END

-- Add to trusted list
EXEC sys.sp_add_trusted_assembly 
    @hash = @hash,
    @description = N'FastWrappers_TSQL Assembly';
GO

-- Now load the assembly (it's already trusted)
CREATE ASSEMBLY [FastWrappers_TSQL]
FROM __ASSEMBLY_FROM_0X__
WITH PERMISSION_SET = UNSAFE;
GO

PRINT 'Assembly loaded and added to trusted list.';
GO

-- =====================================================================
-- STEP 4: Create CLR Function - EncryptString
-- =====================================================================
USE [FastWrappers-TSQL];
GO

IF EXISTS (SELECT * FROM sys.objects WHERE name = 'EncryptString' AND type IN ('FN', 'TF', 'IF', 'FS'))
BEGIN
    DROP FUNCTION [dbo].[EncryptString];
END
GO

CREATE FUNCTION [dbo].[EncryptString](@plainText [nvarchar](4000))
RETURNS [nvarchar](4000)
WITH EXECUTE AS CALLER
AS EXTERNAL NAME [FastWrappers_TSQL].[FastWrapper.FastTransferCLR].[EncryptString];
GO

PRINT 'Function dbo.EncryptString created successfully.';
GO

-- =====================================================================
-- STEP 5: Create CLR Stored Procedure - xp_RunFastTransfer_secure
-- =====================================================================

IF EXISTS (SELECT * FROM sys.objects WHERE name = 'xp_RunFastTransfer_secure' AND type = 'PC')
BEGIN
    DROP PROCEDURE [dbo].[xp_RunFastTransfer_secure];
END
GO

CREATE PROCEDURE [dbo].[xp_RunFastTransfer_secure]
	@fastTransferDir [nvarchar](max),
	@sourceConnectionType [nvarchar](30),
	@sourceConnectStringSecure [nvarchar](4000) = NULL,
	@sourceServer [nvarchar](255),
	@sourceDSN [nvarchar](255) = NULL,
	@sourceProvider [nvarchar](1000) = NULL,
	@isSourceTrusted [bit] = 0,
	@sourceUser [nvarchar](1000) = NULL,
	@sourcePasswordSecure [nvarchar](255) = NULL,
	@sourceDatabase [nvarchar](1000),
	@fileInput [nvarchar](4000) = NULL,
	@query [nvarchar](4000) = NULL,
	@sourceSchema [nvarchar](255) = NULL,
	@sourceTable [nvarchar](255) = NULL,
	@targetConnectionType [nvarchar](30),
	@targetConnectStringSecure [nvarchar](4000) = NULL,
	@targetServer [nvarchar](255),
	@isTargetTrusted [bit] = 0,
	@targetUser [nvarchar](1000) = NULL,
	@targetPasswordSecure [nvarchar](255) = NULL,
	@targetDatabase [nvarchar](255),
	@targetSchema [nvarchar](255),
	@targetTable [nvarchar](255),
	@loadMode [nvarchar](50),
	@batchSize [int] = 1048576,
	@useWorkTables [bit] = 0,
	@method [nvarchar](50) = N'None',
	@distributeKeyColumn [nvarchar](255) = NULL,
	@dataDrivenQuery [nvarchar](4000) = NULL,
	@degree [int] = 4,
	@mapmethod [nvarchar](50) = N'Position',
	@runId [nvarchar](255) = NULL,
	@settingsFile [nvarchar](4000) = NULL,
	@debug [bit] = 0,
	@noBanner [bit] = 0,
	@license [nvarchar](4000) = NULL,
	@loglevel [nvarchar](50) = N'information'
WITH EXECUTE AS CALLER
AS EXTERNAL NAME [FastWrappers_TSQL].[FastWrapper.FastTransferCLR].[RunFastTransfer_Secure];
GO

PRINT 'Stored procedure dbo.xp_RunFastTransfer_secure created successfully.';
GO

-- =====================================================================
-- STEP 6: Create CLR Stored Procedure - xp_RunFastBCP_secure
-- =====================================================================

IF EXISTS (SELECT * FROM sys.objects WHERE name = 'xp_RunFastBCP_secure' AND type = 'PC')
BEGIN
    DROP PROCEDURE [dbo].[xp_RunFastBCP_secure];
END
GO

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
	@applicationintent [nvarchar](20) = N'ReadOnly',
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
	@parquetcompression [nvarchar](20) = N'zstd',
	@degree [int] = -2,
	@method [nvarchar](50) = N'None',
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
	@license [nvarchar](4000) = NULL,
	@loglevel [nvarchar](50) = N'Information',
	@nobanner [bit] = 0,
	@debug [bit] = 0
WITH EXECUTE AS CALLER
AS EXTERNAL NAME [FastWrappers_TSQL].[FastWrapper.FastBCPCLR].[RunFastBCP_Secure];
GO

PRINT 'Stored procedure dbo.xp_RunFastBCP_secure created successfully.';
GO

-- =====================================================================
-- STEP 7: Create Security Roles
-- =====================================================================

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'FastTransfer_Executor' AND type = 'R')
BEGIN
    CREATE ROLE [FastTransfer_Executor];
    PRINT 'Role [FastTransfer_Executor] created successfully.';
END
ELSE
BEGIN
    PRINT 'Role [FastTransfer_Executor] already exists.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'FastBCP_Executor' AND type = 'R')
BEGIN
    CREATE ROLE [FastBCP_Executor];
    PRINT 'Role [FastBCP_Executor] created successfully.';
END
ELSE
BEGIN
    PRINT 'Role [FastBCP_Executor] already exists.';
END
GO

-- Grant EXECUTE permissions
GRANT EXECUTE ON dbo.xp_RunFastTransfer_secure TO [FastTransfer_Executor];
GRANT EXECUTE ON dbo.xp_RunFastBCP_secure TO [FastBCP_Executor];
GRANT EXECUTE ON dbo.EncryptString TO [FastTransfer_Executor];
GRANT EXECUTE ON dbo.EncryptString TO [FastBCP_Executor];
GO

PRINT 'Permissions granted to [FastTransfer_Executor] and [FastBCP_Executor] roles.';
GO

-- =====================================================================
-- STEP 8: Verification
-- =====================================================================

PRINT '';
PRINT '=====================================================================';
PRINT 'Installation Summary';
PRINT '=====================================================================';
PRINT '';

-- Check assembly
IF EXISTS (SELECT * FROM sys.assemblies WHERE name = 'FastWrappers_TSQL')
    PRINT '[OK] Assembly [FastWrappers_TSQL] is loaded';
ELSE
    PRINT '[ERROR] Assembly [FastWrappers_TSQL] is NOT loaded!';

-- Check function
IF EXISTS (SELECT * FROM sys.objects WHERE name = 'EncryptString' AND type = 'FS')
    PRINT '[OK] Function [dbo].[EncryptString] is created';
ELSE
    PRINT '[ERROR] Function [dbo].[EncryptString] is NOT created!';

-- Check FastTransfer stored procedure
IF EXISTS (SELECT * FROM sys.objects WHERE name = 'xp_RunFastTransfer_secure' AND type = 'PC')
    PRINT '[OK] Stored procedure [dbo].[xp_RunFastTransfer_secure] is created';
ELSE
    PRINT '[ERROR] Stored procedure [dbo].[xp_RunFastTransfer_secure] is NOT created!';

-- Check FastBCP stored procedure
IF EXISTS (SELECT * FROM sys.objects WHERE name = 'xp_RunFastBCP_secure' AND type = 'PC')
    PRINT '[OK] Stored procedure [dbo].[xp_RunFastBCP_secure] is created';
ELSE
    PRINT '[ERROR] Stored procedure [dbo].[xp_RunFastBCP_secure] is NOT created!';

-- Check roles
IF EXISTS (SELECT * FROM sys.database_principals WHERE name = 'FastTransfer_Executor' AND type = 'R')
    PRINT '[OK] Role [FastTransfer_Executor] is created';
ELSE
    PRINT '[ERROR] Role [FastTransfer_Executor] is NOT created!';

IF EXISTS (SELECT * FROM sys.database_principals WHERE name = 'FastBCP_Executor' AND type = 'R')
    PRINT '[OK] Role [FastBCP_Executor] is created';
ELSE
    PRINT '[ERROR] Role [FastBCP_Executor] is NOT created!';

PRINT '';
PRINT '=====================================================================';
PRINT 'Next Steps';
PRINT '=====================================================================';
PRINT '1. Add users to the executor roles:';
PRINT '   ALTER ROLE [FastTransfer_Executor] ADD MEMBER [YourUserName];';
PRINT '   ALTER ROLE [FastBCP_Executor] ADD MEMBER [YourUserName];';
PRINT '';
PRINT '2. Test the EncryptString function:';
PRINT '   SELECT dbo.EncryptString(''TestPassword'');';
PRINT '';
PRINT '3. Run a test migration with FastTransfer:';
PRINT '   EXEC dbo.xp_RunFastTransfer_secure @fastTransferDir = ''...'', ...';
PRINT '';
PRINT '4. Run a test export with FastBCP:';
PRINT '   EXEC dbo.xp_RunFastBCP_secure @fastBCPDir = ''...'', ...';
PRINT '';
PRINT '=====================================================================';
GO
