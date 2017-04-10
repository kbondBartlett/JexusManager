﻿// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Web.Administration
{
    using System.Collections.ObjectModel;
#if !__MonoCS__
    using System.Management.Automation;
#endif
    using System.Runtime.InteropServices;
    using System.Xml;
    public sealed class Site : ConfigurationElement
    {
        private const string command = "/config:\"{0}\" /siteid:{1} /systray:false /trace:error";
        private ApplicationCollection _collection;
        private BindingCollection _bindings;
        private SiteLogFile _logFile;
        private SiteLimits _limits;
        private SiteTraceFailedRequestsLogging _trace;
        private VirtualDirectoryDefaults _virtualDefaults;
        private ObjectState? _state;

        internal Site(SiteCollection parent)
            : this(null, parent)
        { }

        internal Site(ConfigurationElement element, SiteCollection parent)
            : base(element, "site", null, parent, null, null)
        {
            ApplicationDefaults = ChildElements["applicationDefaults"] == null
                ? new ApplicationDefaults(parent.ChildElements["applicationDefaults"], this)
                : new ApplicationDefaults(ChildElements["applicationDefaults"], this);
            Parent = parent;
            if (element == null)
            {
                return;
            }

            foreach (ConfigurationElement node in (ConfigurationElementCollection)element)
            {
                var app = new Application(node, Applications);
                Applications.InternalAdd(app);
            }
        }

        public ApplicationDefaults ApplicationDefaults { get; private set; }

        public ApplicationCollection Applications
        {
            get { return _collection ?? (_collection = new ApplicationCollection(this)); }
            internal set { _collection = value; }
        }

        public BindingCollection Bindings
        {
            get { return _bindings ?? (_bindings = new BindingCollection(ChildElements["bindings"], this)); }
            internal set { _bindings = value; }
        }

        public long Id
        {
            get { return (uint)this["id"]; }
            set { this["id"] = value; }
        }

        public SiteLimits Limits
        {
            get { return _limits ?? (_limits = new SiteLimits(ChildElements["limits"], this)); }
        }

        public SiteLogFile LogFile
        {
            get { return _logFile ?? (_logFile = new SiteLogFile(ChildElements["logFile"], this)); }
        }

        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }

        public bool ServerAutoStart
        {
            get { return (bool)this["serverAutoStart"]; }
            set { this["serverAutoStart"] = value; }
        }

        public ObjectState State
        {
            get
            {
                if (_state == null)
                {
                    var result = AsyncHelper.RunSync(GetStateAsync);
                    _state = result ? ObjectState.Started : ObjectState.Stopped;
                }

                return _state.Value;
            }

            internal set
            {
                _state = value;
            }
        }

        internal string CommandLine
        {
            get { return string.Format(command, FileContext.FileName, Id); }
        }

        public SiteTraceFailedRequestsLogging TraceFailedRequestsLogging
        {
            get { return _trace ?? (_trace = new SiteTraceFailedRequestsLogging(ChildElements["traceFailedRequestsLogging"], this)); }
        }

        public VirtualDirectoryDefaults VirtualDirectoryDefaults
        {
            get { return _virtualDefaults ?? (_virtualDefaults = new VirtualDirectoryDefaults(ChildElements["virtualDirectoryDefaults"], this)); }
        }

        internal SiteCollection Parent { get; private set; }

        public Configuration GetWebConfiguration()
        {
            foreach (Application app in Applications)
            {
                if (app.Path == Application.RootPath)
                {
                    return app.GetWebConfiguration();
                }
            }

            return new Configuration(new FileContext(Server, null, null, Name, true, true, true, (Entity as IXmlLineInfo).LineNumber));
        }

        public ObjectState Start()
        {
            // TODO: add timeout.
            return AsyncHelper.RunSync(StartAsync);
        }

        public async Task<ObjectState> StartAsync()
        {
            State = ObjectState.Starting;
            await Server.StartAsync(this);
            return State;
        }

        public ObjectState Stop()
        {
            // TODO: add timeout.
            return AsyncHelper.RunSync(StopAsync);
        }

        public async Task<ObjectState> StopAsync()
        {
            State = ObjectState.Stopping;
            await Server.StopAsync(this);
            return State;
        }

        public override string ToString()
        {
            return Name;
        }

        internal async Task RemoveApplicationsAsync()
        {
            foreach (Application application in Applications)
            {
                await application.RemoveAsync();
            }

            Applications = new ApplicationCollection(this);
        }

        internal ServerManager Server
        {
            get { return Parent.Parent; }
        }

        internal async Task<IEnumerable<DirectoryInfo>> GetPhysicalDirectoriesAsync()
        {
            if (Server.Mode != WorkingMode.Jexus)
            {
                var root = Applications[0].VirtualDirectories[0].PhysicalPath.ExpandIisExpressEnvironmentVariables();
                if (Directory.Exists(root))
                {
                    var result = new DirectoryInfo(root).GetDirectories();
                    return result;
                }

                return new DirectoryInfo[0];
            }

            return null;
        }

        internal async Task RestartAsync()
        {
            await StopAsync();
            await StartAsync();
        }

        internal async Task<bool> GetStateAsync()
        {
            return await Server.GetSiteStateAsync(this);
        }

        internal async Task RemoveApplicationAsync(Application application)
        {
            Applications = await application.RemoveAsync();
        }

        internal void Save()
        {
            foreach (Application application in Applications)
            {
                application.Save();
            }
        }
    }
}
