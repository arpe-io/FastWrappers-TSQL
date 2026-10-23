
namespace FastWrapper
{

	using Microsoft.SqlServer.Server;
	using System;
	using System.Data;
	using System.Data.SqlTypes;
	using System.Diagnostics;
	using System.IO;

	public static class FastTransferCLR
	{

		// --------------------------------------------------------------------
		// 4) Fonction CLR : EncryptString
		// --------------------------------------------------------------------
		// Permet de renvoyer la version chiffrée (Base64) d’une chaîne en clair
		// à partir de T-SQL.
		// --------------------------------------------------------------------
		[SqlFunction]
		public static SqlString EncryptString(SqlString plainText)
		{
			if (plainText.IsNull) return SqlString.Null;
			string encrypted = KeyProvider.AesEncrypt(plainText.Value);
			return new SqlString(encrypted);
		}



		// --------------------------------------------------------------------
		// RunFastTransfer_Secure :
		// Toutes les valeurs sensibles (password, connectString) sont
		// toujours passées en version chiffrée (Base64).
		// On déchiffre avant la logique de construction + exécution du binaire.
		// --------------------------------------------------------------------
		[SqlProcedure]
		public static void RunFastTransfer_Secure(
			SqlString fastTransferDir,

			// 2) Source Connection 
			SqlString sourceConnectionType,
			SqlString sourceConnectStringSecure,   // chiffré
			SqlString sourceServer,
			SqlString sourceDSN,
			SqlString sourceProvider,
			SqlBoolean isSourceTrusted,
			SqlString sourceUser,
			SqlString sourcePasswordSecure,        // chiffré
			SqlString sourceDatabase,

			// 3) Source Infos
			SqlString fileInput,
			SqlString query,
			SqlString sourceSchema,
			SqlString sourceTable,

			// 4) Target Connection 
			SqlString targetConnectionType,
			SqlString targetConnectStringSecure,   // chiffré
			SqlString targetServer,
			SqlBoolean isTargetTrusted,
			SqlString targetUser,
			SqlString targetPasswordSecure,        // chiffré
			SqlString targetDatabase,

			// 5) Target Infos
			SqlString targetSchema,
			SqlString targetTable,
			SqlString loadMode,
			SqlInt32 batchSize,
			SqlBoolean useWorkTables,

			// 6) Advanced
			SqlString method,
			SqlString distributeKeyColumn,
			SqlString dataDrivenQuery,
			SqlInt32 degree,
			SqlString mapMethod,

			// 7) Logging
			SqlString runId,
			SqlString settingsFile,
			SqlBoolean debug,
			SqlBoolean noBanner,

			// 8) License (optional, can be null or empty if not required
			SqlString license,
			SqlString loglevel

		)
		{



			
			//Déchiffrer systématiquement les valeurs sensibles s'il y en a 




			string sourceConnectString = null;
			if (!(sourceConnectStringSecure.IsNull))
			{
				sourceConnectString = KeyProvider.AesDecrypt((string)sourceConnectStringSecure);
			}

			string sourcePassword = null;
			if (!(sourcePasswordSecure.IsNull))
			{
				sourcePassword = KeyProvider.AesDecrypt((string)sourcePasswordSecure);
			}

			string targetConnectString = null;
			if (!(targetConnectStringSecure.IsNull))
			{
				targetConnectString = KeyProvider.AesDecrypt((string)targetConnectStringSecure);
			}

			string targetPassword = null;
			if (!(targetPasswordSecure.IsNull))
			{
				targetPassword = KeyProvider.AesDecrypt((string)targetPasswordSecure);
			}

			// use try catch to manage error and cancellation from client

			RunFastTransferInternal(
			fastTransferDir,
			sourceConnectionType,
			sourceConnectString,
			sourceServer,
			sourceDSN,
			sourceProvider,
			isSourceTrusted,
			sourceUser,
			sourcePassword,
			sourceDatabase,
			fileInput,
			query,
			sourceSchema,
			sourceTable,
			targetConnectionType,
			targetConnectString,
			targetServer,
			isTargetTrusted,
			targetUser,
			targetPassword,
			targetDatabase,
			targetSchema,
			targetTable,
			loadMode,
			batchSize,
			useWorkTables,
			method,
			distributeKeyColumn,
			dataDrivenQuery,
			degree,
			mapMethod,
			runId,
			settingsFile,			
			debug,
			noBanner,
			license,
			loglevel
			);
		}

		private static void RunFastTransferInternal(
		SqlString fastTransferDir,		// 1) FastTransfer Binary Directory/Path e.g. ".\" or "C:\\tools\\MyFastTransfer" or "./fasttransfer"
		SqlString sourceConnectionType, // must be one of [clickhouse, duckdb, duckdbstream, hana, mssql, mysql, nzsql, odbc, oledb, oraodp, pgcopy, pgsql, teradata]
		SqlString sourceConnectString,	// if provided, we skip server/user/password/etc.
		SqlString sourceServer,			// e.g. "Host", "Host:Port", "Host\\Instance", "Host:Port/TNSService"
		SqlString sourceDSN,            // optional DSN name, if using ODBC
		SqlString sourceProvider,       // optional, for OLEDB only
		SqlBoolean isSourceTrusted,
		SqlString sourceUser,
		SqlString sourcePassword,
		SqlString sourceDatabase,
		SqlString fileInput,            // e.g. "C:\\path\\to\\file.sql"
		SqlString query,                // e.g. "SELECT * FROM table"
		SqlString sourceSchema,         // e.g. "dbo" . Required for some parallel methods (RowId, Ctid, Ntile, RangeId)
		SqlString sourceTable,          // e.g. "MyTable" . Required for some parallel methods (RowId, Ctid, Ntile, RangeId)
		SqlString targetConnectionType, // must be one of [clickhousebulk, duckdb, hanabulk, msbulk, mysqlbulk, nzbulk, orabulk, oradirect, pgcopy, teradata]
		SqlString targetConnectString,
		SqlString targetServer,
		SqlBoolean isTargetTrusted,
		SqlString targetUser,
		SqlString targetPassword,
		SqlString targetDatabase,
		SqlString targetSchema,         // Mandatory. eg "public"
		SqlString targetTable,          // Mandatory. eg "CopyTable"
		SqlString loadMode,             // "Append" or "Truncate"
		SqlInt32 batchSize,             // e.g. 130000
		SqlBoolean useWorkTables,       // use staging work tables before final insert
		SqlString method,               // distribution method for parallelism :"None", "Random", "DataDriven", "DBPartition", "RangeId", "Ntile", "Timepartition", "Ctid", "Rowid", "Physloc", "NZDataSlice"
		SqlString distributeKeyColumn,  // required if method in ["Random","DataDriven","RangeId","Ntile","Timepartition"]. For Timepartition : (datecolumn,year) | (datecolumn,year,month) | (datecolumn,year,month,day) | (datecolumn,year,week)
		SqlString dataDrivenQuery,      // custom query for DataDriven method returning distinct values for distribution
		SqlInt32 degree,                // concurrency degree if method != "None" useless if method = "None" should be != 1, can be less than 0 for dynamic degree (based on cpucount on the platform where FastTransfer is running. -2 = CpuCount/2)
		SqlString mapMethod,            // "Position"(default) or "Name" (Automatic mapping of columns based on names (case insensitive) with tolerance on the order of columns. Non present columns in source or target are ignored. Name may mot be available for all target types (see doc))
		SqlString runId,                // a run identifier for logging (can be a string for grouping or a unique identifier). Guid is used if not provide
		SqlString settingsFile,         // path for a custom FastTransfer_Settings.json file, for custom logging		
		SqlBoolean debug,               // for debugging purpose, if true, the FastTransfer_Settings.json file is created in the current directory with the default settings
		SqlBoolean noBanner,            // suppress FastTransfer banner and version information at startup
		SqlString license,              // license key file or url for FastTransfer (optional, can be null or empty if not required)
		SqlString loglevel				// optional, can be "error", "warning", "information", "debug" (default is "information")
			)
		
		{
			// --------------------------------------------------------------------
			// Convert SqlTypes to .NET types
			// --------------------------------------------------------------------
			string exeDir = fastTransferDir.IsNull ? null : fastTransferDir.Value.Trim();
			string srcConnType = sourceConnectionType.IsNull ? null : sourceConnectionType.Value.Trim().ToLowerInvariant();
			string srcConnStr = sourceConnectString.IsNull ? null : sourceConnectString.Value.Trim();

			string srcServerVal = sourceServer.IsNull ? null : sourceServer.Value.Trim();
			string srcDsn = sourceDSN.IsNull ? null : sourceDSN.Value.Trim();
			string srcProvider = sourceProvider.IsNull ? null : sourceProvider.Value.Trim();
			bool srcTrusted = !isSourceTrusted.IsNull && isSourceTrusted.Value;
			string srcUserVal = sourceUser.IsNull ? null : sourceUser.Value.Trim();
			string srcPasswordVal = sourcePassword.IsNull ? null : sourcePassword.Value.Trim();
			string srcDatabaseVal = sourceDatabase.IsNull ? null : sourceDatabase.Value.Trim();

			string fileInputVal = fileInput.IsNull ? null : fileInput.Value.Trim();
			string queryVal = query.IsNull ? null : query.Value.Trim();
			string srcSchemaVal = sourceSchema.IsNull ? null : sourceSchema.Value.Trim();
			string srcTableVal = sourceTable.IsNull ? null : sourceTable.Value.Trim();

			string tgtConnType = targetConnectionType.IsNull ? null : targetConnectionType.Value.Trim().ToLowerInvariant();
			string tgtConnStr = targetConnectString.IsNull ? null : targetConnectString.Value.Trim();

			string tgtServerVal = targetServer.IsNull ? null : targetServer.Value.Trim();
			bool tgtTrusted = !isTargetTrusted.IsNull && isTargetTrusted.Value;
			string tgtUserVal = targetUser.IsNull ? null : targetUser.Value.Trim();
			string tgtPasswordVal = targetPassword.IsNull ? null : targetPassword.Value.Trim();
			string tgtDatabaseVal = targetDatabase.IsNull ? null : targetDatabase.Value.Trim();

			string tgtSchemaVal = targetSchema.IsNull ? null : targetSchema.Value.Trim();
			string tgtTableVal = targetTable.IsNull ? null : targetTable.Value.Trim();
			string loadModeVal = loadMode.IsNull ? null : loadMode.Value.Trim();
			int? batchSizeVal = batchSize.IsNull ? (int?)null : batchSize.Value;
		bool useWorkTablesVal = !useWorkTables.IsNull && useWorkTables.Value;

		string methodVal = method.IsNull ? null : method.Value.Trim();
		string distKeyColVal = distributeKeyColumn.IsNull ? null : distributeKeyColumn.Value.Trim();
		string dataDrivenQueryVal = dataDrivenQuery.IsNull ? null : dataDrivenQuery.Value.Trim();
		int? degreeVal = degree.IsNull ? (int?)null : degree.Value;
		string mapMethodVal = mapMethod.IsNull ? null : mapMethod.Value.Trim();

		string runIdVal = runId.IsNull ? null : runId.Value.Trim();
		string settingsFileVal = settingsFile.IsNull ? null : settingsFile.Value.Trim();

		bool debugVal = !debug.IsNull && debug.Value ? true : false;
		bool noBannerVal = !noBanner.IsNull && noBanner.Value;
		// If license is provided, it can be null or empty if not required
			string licenseVal = license.IsNull ? null : license.Value.Trim();

			// Default loglevel to "information"
			string loglevelVal = "information";
			// If provided, validate loglevel
			if (!loglevel.IsNull)
			{
				string loglevelInput = loglevel.Value.Trim().ToLowerInvariant();
				string[] validLogLevels = { "error", "warning", "information", "debug" };
				if (Array.IndexOf(validLogLevels, loglevelInput) < 0)
				{
					throw new ArgumentException($"Invalid loglevel: '{loglevelInput}'. Use 'error', 'warning', 'information', or 'debug'. WARNING the parameter is Case Sensitive");
				}
				loglevelVal = loglevelInput;
			}


			// --------------------------------------------------------------------
			// 1. Parameter Validation
			// --------------------------------------------------------------------

			if (string.IsNullOrWhiteSpace(exeDir))
			{
				throw new ArgumentException("fastTransferDir must be provided (directory containing the FastTransfer executable).");
			}

			// Validate SourceConnectionType
			string[] validSourceTypes = {
			"clickhouse", "duckdb", "duckdbstream", "hana", "mssql", "mysql",
			"nzsql", "odbc", "oledb", "oraodp", "pgcopy", "pgsql", "teradata"};
			if (Array.IndexOf(validSourceTypes, srcConnType) < 0)
			{
				throw new ArgumentException($"Invalid SourceConnectionType: '{srcConnType}'. Possible value are {string.Join(", ", validSourceTypes)}.");
			}

			// Validate TargetConnectionType
			string[] validTargetTypes = {
			"clickhousebulk", "duckdb", "hanabulk", "msbulk", "mysqlbulk",
			"nzbulk", "orabulk", "oradirect", "pgcopy", "teradata"};
			if (Array.IndexOf(validTargetTypes, tgtConnType) < 0)
			{
				throw new ArgumentException($"Invalid TargetConnectionType: '{tgtConnType}'. Possible value are {string.Join(", ", validTargetTypes)}.");
			}

			// Source: either use connect string or explicit params
			bool hasSrcConnString = !string.IsNullOrEmpty(srcConnStr);
			if (hasSrcConnString)
			{
				// If using connect string, no server/DSN/provider/trusted user/pwd/database
				if (!string.IsNullOrEmpty(srcServerVal) ||
					!string.IsNullOrEmpty(srcDatabaseVal) ||
					!string.IsNullOrEmpty(srcUserVal) ||
					!string.IsNullOrEmpty(srcPasswordVal) ||
					!string.IsNullOrEmpty(srcDsn))
				{
					throw new ArgumentException("When sourceConnectString is provided, do not supply server/DSN/credentials/database.");
				}
			}
			else
			{
				// Not using connect string: must provide server/DSN and database
				if (string.IsNullOrEmpty(srcServerVal) && string.IsNullOrEmpty(srcDsn))
				{
					throw new ArgumentException("Must provide either sourceServer or sourceDSN if sourceConnectString is NULL.");
				}
				if (string.IsNullOrEmpty(srcDatabaseVal))
				{
					throw new ArgumentException("Must provide sourceDatabase when not using sourceConnectString.");
				}
				if (!srcTrusted)
				{
					if (string.IsNullOrEmpty(srcUserVal) || string.IsNullOrEmpty(srcPasswordVal))
					{
						throw new ArgumentException("Must provide sourceUser and sourcePassword when sourceTrusted = 0.");
					}
				}
			}

			// SourceInfos: at least one of fileInput, query, or (schema+table)
			int sourceChoiceCount = 0;
			if (!string.IsNullOrEmpty(fileInputVal)) sourceChoiceCount++;
			if (!string.IsNullOrEmpty(queryVal)) sourceChoiceCount++;
			if (!string.IsNullOrEmpty(srcSchemaVal) && !string.IsNullOrEmpty(srcTableVal)) sourceChoiceCount++;
			if (sourceChoiceCount == 0)
			{
				throw new ArgumentException("You must supply at least one of fileInput, query, or (sourceSchema AND sourceTable).");
			}
			else
			{
				// SourceInfos: exactly one of fileInput or query (if provided) (table +schema is optional)
				int sourceChoiceCountfileandquery = 0;
				if (!string.IsNullOrEmpty(fileInputVal)) sourceChoiceCountfileandquery++;
				if (!string.IsNullOrEmpty(queryVal)) sourceChoiceCountfileandquery++;
				if (sourceChoiceCountfileandquery > 1)
				{
					throw new ArgumentException("You must supply only one of fileInput or query");
				}
			}


				// Target: either use connect string or explicit params
				bool hasTgtConnString = !string.IsNullOrEmpty(tgtConnStr);
			if (hasTgtConnString)
			{
				if (!string.IsNullOrEmpty(tgtServerVal) ||
					!string.IsNullOrEmpty(tgtDatabaseVal) ||
					!string.IsNullOrEmpty(tgtUserVal) ||
					!string.IsNullOrEmpty(tgtPasswordVal))
				{
					throw new ArgumentException("When targetConnectString is provided, do not supply server/credentials/database.");
				}
			}
			else
			{
				if (string.IsNullOrEmpty(tgtServerVal))
				{
					throw new ArgumentException("Must provide targetServer if targetConnectString is NULL.");
				}
				if (string.IsNullOrEmpty(tgtDatabaseVal))
				{
					throw new ArgumentException("Must provide targetDatabase if targetConnectString is NULL.");
				}
				if (!tgtTrusted)
				{
					if (string.IsNullOrEmpty(tgtUserVal) || string.IsNullOrEmpty(tgtPasswordVal))
					{
						throw new ArgumentException("Must provide targetUser and targetPassword when targetTrusted = 0.");
					}
				}
			}

			if (string.IsNullOrEmpty(tgtSchemaVal) || string.IsNullOrEmpty(tgtTableVal))
			{
				throw new ArgumentException("Must provide targetSchema and targetTable.");
			}

			// Advanced Parameters
			bool hasMethod = !string.IsNullOrEmpty(methodVal);
			if (hasMethod)
			{
				string[] validMethods = { "None", "Random", "DataDriven", "DBPartition", "RangeId", "Ntile", "Timepartition", "Ctid", "Rowid", "Physloc", "NZDataSlice" };
				if (Array.IndexOf(validMethods, methodVal) < 0)
				{
					throw new ArgumentException($"Invalid method: '{methodVal}'. use 'None', 'Random', 'DataDriven', 'DBPartition', 'RangeId', 'Ntile', 'Timepartition', 'Ctid', 'Rowid', 'Physloc' or 'NZDataSlice'. WARNING the parameter is Case Sensitive");
				}

				if (!methodVal.Equals("None"))
				{
					// If method in [Random, DataDriven, RangeId, Ntile, Timepartition], must have distributeKeyColumn
					if (methodVal.Equals("Random") ||
						methodVal.Equals("DataDriven") ||
						methodVal.Equals("RangeId") ||
						methodVal.Equals("Ntile") ||
						methodVal.Equals("Timepartition"))
					{
						if (string.IsNullOrEmpty(distKeyColVal))
						{
							throw new ArgumentException($"When method is '{methodVal}', you must provide --distributeKeyColumn.");
						}
					}

					if (!degreeVal.HasValue)
					{
						throw new ArgumentException("When specifying a method other than None, you must provide degree.");
					}
				}
			}

			bool hasMode = !string.IsNullOrEmpty(loadModeVal);
			if (hasMode)
			{
				string[] validModes = { "Append", "Truncate" };
				if (Array.IndexOf(validModes, loadModeVal) < 0)
				{
					throw new ArgumentException($"Invalid loadmode: '{loadModeVal}'. Use 'Append' or 'Truncate'.  WARNING the parameter is Case Sensitive");
				}
			}

			bool hasMapMethod = !string.IsNullOrEmpty(mapMethodVal);
			if (hasMapMethod)
			{
				string[] validMapMethods = { "Position", "Name" };
				if (Array.IndexOf(validMapMethods, mapMethodVal) < 0)
				{
					throw new ArgumentException($"Invalid mapMethod: '{mapMethodVal}'. use 'Position' or 'Name'.  WARNING the parameter is Case Sensitive");
				}
			}

			// --------------------------------------------------------------------
			// 2. Build the path to the FastTransfer tool
			// --------------------------------------------------------------------
			// Ensure we have the actual EXE name or './fasttransfer' at the end
			string exePath = exeDir;
			// If not specifically ending with "FastTransfer.exe" or "fasttransfer",
			// try appending "FastTransfer.exe" automatically:
			// (an existing directory such as "D:\FastTransfer" is always a directory, even if it ends with "fasttransfer")
			if (Directory.Exists(exePath) ||
				(!exePath.EndsWith("FastTransfer.exe", StringComparison.OrdinalIgnoreCase) &&
				!exePath.EndsWith("fasttransfer", StringComparison.OrdinalIgnoreCase)))
			{
				if (!exePath.EndsWith("\\") && !exePath.EndsWith("/"))
				{
					exePath += Path.DirectorySeparatorChar;
				}
				exePath += "FastTransfer.exe";
			}


			// --------------------------------------------------------------------
			// 3. Construct Command-Line Arguments
			// --------------------------------------------------------------------
			// We'll build this up in a string. We'll quote all values that might have spaces.
			// The final invocation is: "exePath" <arguments>

			// Helper local function for quoting arguments
			string Q(string val) => $"\"{val}\"";

			// Start with empty arguments
			string args = string.Empty;

			// --sourceconnectiontype
			args += $" --sourceconnectiontype {Q(srcConnType)}";

			// Source connection parameters
			if (hasSrcConnString)
			{
				// --sourceconnectstring
				args += $" --sourceconnectstring {Q(srcConnStr)}";
			}
			else
			{
				// Either server or DSN
				if (!string.IsNullOrEmpty(srcServerVal))
				{
					args += $" --sourceserver {Q(srcServerVal)}";

					// If provider is not null, add it
					if (!string.IsNullOrEmpty(srcProvider))
					{
						args += $" --sourceprovider {Q(srcProvider)}";
					}
				}
				else
				{
					// Then must be DSN
					args += $" --sourcedsn {Q(srcDsn)}";
				}

				if (srcTrusted)
				{
					args += " --sourcetrusted";
				}
				else
				{
					args += $" --sourceuser {Q(srcUserVal)} --sourcepassword {Q(srcPasswordVal)}";
				}

				args += $" --sourcedatabase {Q(srcDatabaseVal)}";
			}

			// Source Infos
			if (!string.IsNullOrEmpty(fileInputVal))
			{
				args += $" --fileinput {Q(fileInputVal)}";
			}
			else if (!string.IsNullOrEmpty(queryVal))
			{
				args += $" --query {Q(queryVal)}";
			}
			
			if (!string.IsNullOrEmpty(srcTableVal) && !string.IsNullOrEmpty(srcSchemaVal))
			{
				args += $" --sourceschema {Q(srcSchemaVal)}";
				args += $" --sourcetable {Q(srcTableVal)}";
			}

			// --targetconnectiontype
			args += $" --targetconnectiontype {Q(tgtConnType)}";

			// Target connection parameters
			if (hasTgtConnString)
			{
				// --targetconnectstring
				args += $" --targetconnectstring {Q(tgtConnStr)}";
			}
			else
			{
				args += $" --targetserver {Q(tgtServerVal)}";

				if (tgtTrusted)
				{
					args += " --targettrusted";
				}
				else
				{
					args += $" --targetuser {Q(tgtUserVal)} --targetpassword {Q(tgtPasswordVal)}";
				}

				args += $" --targetdatabase {Q(tgtDatabaseVal)}";
			}

			// Target Infos
			args += $" --targetschema {Q(tgtSchemaVal)}";
			args += $" --targettable {Q(tgtTableVal)}";
			if (!string.IsNullOrEmpty(loadModeVal))
			{
				args += $" --loadmode {Q(loadModeVal)}";
			}
			if (batchSizeVal.HasValue)
			{
				args += $" --batchsize {batchSizeVal.Value}";
			}
			if (useWorkTablesVal)
			{
				args += " --useworktables";
			}

			// Advanced Parameters
			if (hasMethod)
			{
				// e.g. --method "RangeId" ...
				// If methodVal = "None" =>  --method None
				args += $" --method {Q(methodVal)}";
				if (!methodVal.Equals("None", StringComparison.OrdinalIgnoreCase))
				{
					// If it's one of the partition-based
					if (methodVal.Equals("random", StringComparison.OrdinalIgnoreCase) ||
						methodVal.Equals("datadriven", StringComparison.OrdinalIgnoreCase) ||
						methodVal.Equals("rangeid", StringComparison.OrdinalIgnoreCase) ||
						methodVal.Equals("ntile", StringComparison.OrdinalIgnoreCase) ||
						methodVal.Equals("timepartition", StringComparison.OrdinalIgnoreCase))
					{
						args += $" --distributeKeyColumn {Q(distKeyColVal)}";
					}
					args += $" --degree {degreeVal.Value}";
				}
			}
		if (!string.IsNullOrEmpty(dataDrivenQueryVal))
		{
			args += $" --datadrivenquery {Q(dataDrivenQueryVal)}";
		}
		if (hasMapMethod)
		{
			args += $" --mapmethod {Q(mapMethodVal)}";
		}

		// Log Parameters
		if (!string.IsNullOrEmpty(runIdVal))
		{
			args += $" --runid {Q(runIdVal)}";
		}
		if (!string.IsNullOrEmpty(settingsFileVal))
		{
			args += $" --settingsfile {Q(settingsFileVal)}";
		}

		// License (optional)
		if (!string.IsNullOrEmpty(licenseVal))
	{
		args += $" --license {Q(licenseVal)}";
	}

	// Loglevel
	if (!string.IsNullOrEmpty(loglevelVal))
	{
		args += $" --loglevel {Q(loglevelVal)}";
	}
	if (noBannerVal)
	{
		args += " --nobanner";
	}

	// Trim leading space
	args = args.Trim();

	//args4Log : for logging purpose, remove password or passwd info in the args string using regexp
	string args4Log = args;
	args4Log = System.Text.RegularExpressions.Regex.Replace(args4Log, @"--sourcepassword\s+""[^""]*""", "--sourcepassword \"<hidden>\"");
	args4Log = System.Text.RegularExpressions.Regex.Replace(args4Log, @"--targetpassword\s+""[^""]*""", "--targetpassword \"<hidden>\"");
	args4Log = System.Text.RegularExpressions.Regex.Replace(args4Log, @"--sourceconnectstring\s+""[^""]*""", "--sourceconnectstring \"<hidden>\"");
	args4Log = System.Text.RegularExpressions.Regex.Replace(args4Log, @"--targetconnectstring\s+""[^""]*""", "--targetconnectstring \"<hidden>\"");

	if (debugVal)
	{			
		//print the command line exe and args
		SqlContext.Pipe.Send("FastTransfer Command " + exePath + " " + args4Log + Environment.NewLine);				
	}
			// --------------------------------------------------------------------
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = exePath,
				Arguments = args,
				UseShellExecute = false, // maybe true ?
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,				
			};

			


			try
			{
				using (Process proc = Process.Start(psi))
				{
					
					
					// Read output by 
					string stdout = proc.StandardOutput.ReadToEnd();
					string stderr = proc.StandardError.ReadToEnd();

					proc.WaitForExit();
					int exitCode = proc.ExitCode;

					if (debugVal)
					{
						// Send the output back to the SQL client
						if (!string.IsNullOrEmpty(stdout))
						{
							// Diviser la sortie en morceaux de 4000 caractères
							int chunkSize = 4000;
							for (int i = 0; i < stdout.Length; i += chunkSize)
							{
								string chunk = stdout.Substring(i, Math.Min(chunkSize, stdout.Length - i));
								SqlContext.Pipe.Send(chunk);
							}
						}
					}
					// Extract some metrics from the end of the stdout (last 3000 characters) and search for 
					// Total rows : xxxx to get the number of rows processed
					// Total columns : xxxx to get the number of columns processed
					// Total cells : xxxx to get the number of cells processed
					// Total time : Elapsed=xxxx ms to get the total time taken in milliseconds
					// Target table : ... -|- TargetTable -|- Completed Load

					if (stdout.Length > 3000)
					{
						stdout = stdout.Substring(stdout.Length - 3000);
					}

					// Extract metrics from stdout using regex
					string totalRows = System.Text.RegularExpressions.Regex.Match(stdout, @"Total rows\s*:\s*(\d+)").Groups[1].Value;
					string totalColumns = System.Text.RegularExpressions.Regex.Match(stdout, @"Total columns\s*:\s*(\d+)").Groups[1].Value;
					string totalCells = System.Text.RegularExpressions.Regex.Match(stdout, @"Total cells\s*:\s*(\d+)").Groups[1].Value;
					// "Total time : Elapsed=xxx ms" (FastTransfer >= 0.18 also logs "Transfer time : Elapsed=xxx ms" before it)
					string totalTime = System.Text.RegularExpressions.Regex.Match(stdout, @"Total time\s*:\s*Elapsed\s*=\s*(\d+)\s*ms").Groups[1].Value;

					// Send metrics to the SQL client as a table output added to the parameters (exepted the passwords and connect strings)
					SqlDataRecord record = new SqlDataRecord(
						new SqlMetaData("targetdatabase", SqlDbType.NVarChar, 128),
						new SqlMetaData("targetSchema", SqlDbType.NVarChar, 128),
						new SqlMetaData("targetTable", SqlDbType.NVarChar, 128),
						new SqlMetaData("TotalRows", SqlDbType.BigInt),
						new SqlMetaData("TotalColumns", SqlDbType.Int),
						new SqlMetaData("TotalCells", SqlDbType.BigInt),
						new SqlMetaData("TotalTimeMs", SqlDbType.BigInt),
						new SqlMetaData("Status", SqlDbType.Int),
						new SqlMetaData("StdErr", SqlDbType.NVarChar, -1) // -1 for max length (unlimited)
					);

					var errorMsg = string.Empty;
					if (exitCode != 0)
					{
						errorMsg = $"FastTransfer process failed with exit code {exitCode}. See stderr for details.";
						errorMsg += Environment.NewLine + stdout;
					}


					record.SetString(0, tgtDatabaseVal ?? string.Empty);
					record.SetString(1, tgtSchemaVal ?? string.Empty);
					record.SetString(2, tgtTableVal ?? string.Empty);
					record.SetInt64(3, Int64.TryParse(totalRows, out Int64 rows) ? rows : 0);
					record.SetInt32(4, int.TryParse(totalColumns, out int cols) ? cols : 0);
					record.SetInt64(5, Int64.TryParse(totalCells, out Int64 cells) ? cells : 0);
					record.SetInt64(6, Int64.TryParse(totalTime, out Int64 time) ? time : 0);
					record.SetInt32(7, exitCode); // Status: 0 for success, non-zero for error
					record.SetString(8, errorMsg);


					SqlContext.Pipe.SendResultsStart(record);
					SqlContext.Pipe.SendResultsRow(record);
					SqlContext.Pipe.SendResultsEnd();


					if (!string.IsNullOrEmpty(stderr))
					{
						SqlContext.Pipe.Send("FastTransfer stderr:\r\n" + stderr);
					}

					if (exitCode != 0)
					{
						throw new Exception($"FastTransfer process returned exit code {exitCode}.");
					}
				}
			}
			catch (Exception ex)
			{
				throw new Exception("Error invoking FastTransfer: " + ex.Message, ex);
			}
		}
	}

}