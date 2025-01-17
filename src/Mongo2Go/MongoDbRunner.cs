﻿using System;
using Mongo2Go.Helper;

namespace Mongo2Go
{
    /// <summary>
    /// Mongo2Go main entry point
    /// </summary>
    public partial class MongoDbRunner : IDisposable
    {
        private readonly IMongoDbProcess _mongoDbProcess;
        private readonly IFileSystem _fileSystem;
        private readonly string _dataDirectoryWithPort;
        private readonly int _port;
        private readonly IMongoBinaryLocator _mongoBin;

        /// <summary>
        /// State of the current MongoDB instance
        /// </summary>
        public State State { get; private set; }

        /// <summary>
        /// Connections string that should be used to establish a connection the MongoDB instance
        /// </summary>
        public string ConnectionString { get; private set; }

        /// <summary>
        /// Starts Multiple MongoDB instances with each call
        /// On dispose: kills them and deletes their data directory
        /// </summary>
        /// <remarks>Should be used for integration tests</remarks>
        public static MongoDbRunner Start(string dataDirectory = MongoDbDefaults.DataDirectory, string searchPatternOverride = null)
        {
            dataDirectory += Guid.NewGuid().ToString().Replace("-", "").Substring(0, 20);

            return new MongoDbRunner(PortPool.GetInstance, new FileSystem(), new MongoDbProcessStarter(), new MongoBinaryLocator(searchPatternOverride), dataDirectory);
        }

        internal static MongoDbRunner StartUnitTest(IPortPool portPool, IFileSystem fileSystem, IMongoDbProcessStarter processStarter, IMongoBinaryLocator mongoBin)
        {
            return new MongoDbRunner(portPool, fileSystem, processStarter, mongoBin, MongoDbDefaults.DataDirectory);
        }

        /// <summary>
        /// Only starts one single MongoDB instance (even on multiple calls), does not kill it, does not delete data
        /// </summary>
        /// <remarks>
        /// Should be used for local debugging only
        /// WARNING: one single instance on one single machine is not a suitable setup for productive environments!!!
        /// </remarks>
        public static MongoDbRunner StartForDebugging(string dataDirectory = MongoDbDefaults.DataDirectory, string searchPatternOverride = null)
        {
            return new MongoDbRunner(new ProcessWatcher(), new PortWatcher(), new FileSystem(), new MongoDbProcessStarter(), new MongoBinaryLocator(searchPatternOverride), dataDirectory);
        }

        internal static MongoDbRunner StartForDebuggingUnitTest(IProcessWatcher processWatcher, IPortWatcher portWatcher, IFileSystem fileSystem, IMongoDbProcessStarter processStarter, IMongoBinaryLocator mongoBin)
        {
            return new MongoDbRunner(processWatcher, portWatcher, fileSystem, processStarter, mongoBin, MongoDbDefaults.DataDirectory);
        }

        /// <summary>
        /// Executes Mongoimport on the associated MongoDB Instace
        /// </summary>
        public void Import(string database, string collection, string inputFile, bool drop)
        {

            MongoImportExport.Import(_mongoBin.Directory, _port, database, collection, inputFile, drop);
        }

        /// <summary>
        /// Executes Mongoexport on the associated MongoDB Instace
        /// </summary>
        public void Export(string database, string collection, string outputFile)
        {

            MongoImportExport.Export(_mongoBin.Directory, _port, database, collection, outputFile);
        }

        /// <summary>
        /// usage: local debugging
        /// </summary>
        private MongoDbRunner(IProcessWatcher processWatcher, IPortWatcher portWatcher, IFileSystem fileSystem, IMongoDbProcessStarter processStarter, IMongoBinaryLocator mongoBin, string dataDirectory)
        {
            _fileSystem = fileSystem;
            _port = MongoDbDefaults.DefaultPort;
            _mongoBin = mongoBin;

            MakeMongoBinarysExecutable();

            ConnectionString = "mongodb://localhost:{0}/".Formatted(_port);

            if (processWatcher.IsProcessRunning(MongoDbDefaults.ProcessName) && !portWatcher.IsPortAvailable(_port))
            {
                State = State.AlreadyRunning;
                return;
            }

            if (!portWatcher.IsPortAvailable(_port))
            {
                throw new MongoDbPortAlreadyTakenException("MongoDB can't be started. The TCP port {0} is already taken.".Formatted(_port));
            }

            _fileSystem.CreateFolder(dataDirectory);
            _fileSystem.DeleteFile(@"{0}{1}{2}".Formatted(dataDirectory, System.IO.Path.DirectorySeparatorChar.ToString(), MongoDbDefaults.Lockfile));
            _mongoDbProcess = processStarter.Start(_mongoBin.Directory, dataDirectory, _port, true);

            State = State.Running;
        }

        /// <summary>
        /// usage: integration tests
        /// </summary>
        private MongoDbRunner(IPortPool portPool, IFileSystem fileSystem, IMongoDbProcessStarter processStarter, IMongoBinaryLocator mongoBin, string dataDirectory)
        {
            _fileSystem = fileSystem;
            _port = portPool.GetNextOpenPort();
            _mongoBin = mongoBin;

            MakeMongoBinarysExecutable();

            ConnectionString = "mongodb://localhost:{0}/".Formatted(_port);

            _dataDirectoryWithPort = "{0}_{1}".Formatted(dataDirectory, _port);
            _fileSystem.CreateFolder(_dataDirectoryWithPort);
            _fileSystem.DeleteFile(@"{0}{1}{2}".Formatted(_dataDirectoryWithPort, System.IO.Path.DirectorySeparatorChar.ToString(), MongoDbDefaults.Lockfile));

            _mongoDbProcess = processStarter.Start(_mongoBin.Directory, _dataDirectoryWithPort, _port);

            State = State.Running;
        }

        private void MakeMongoBinarysExecutable()
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                _fileSystem.MakeFileExecutable(System.IO.Path.Combine(_mongoBin.Directory, MongoDbDefaults.MongodExecutable));
                _fileSystem.MakeFileExecutable(System.IO.Path.Combine(_mongoBin.Directory, MongoDbDefaults.MongoExportExecutable));
                _fileSystem.MakeFileExecutable(System.IO.Path.Combine(_mongoBin.Directory, MongoDbDefaults.MongoImportExecutable));
            }
        }
    }
}