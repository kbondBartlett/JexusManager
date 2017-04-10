﻿// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Microsoft.Web.Administration
{
    using System.Runtime.InteropServices;
    using System.Xml;

    [DebuggerDisplay("{FileName}[{Location}]")]
    internal sealed class FileContext : IEquatable<FileContext>
    {
        private readonly ServerManager _server;

        private readonly bool _dontThrow;

        internal bool AppHost { get; }
        public bool ReadOnly { get; }

        private ProtectedConfiguration _protectedConfiguration;

        internal FileContext(ServerManager server, string fileName, FileContext parent, string location, bool appHost, bool dontThrow, bool readOnly, int lineNumber = 0)
        {
            _server = server;
            _dontThrow = dontThrow;
            this.AppHost = appHost;
            this.ReadOnly = readOnly;
            this.Locations = new List<Location>();
            FileName = fileName;
            this.Location = location;
            Parent = parent;
            _lineNumber = lineNumber;
        }

        private void Initialize()
        {
            this.Parent?.Initialize();
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            LoadSchemas();
            _rootSectionGroup = new SectionGroup(this);
            this.CloneParentSectionGroups(this.Parent);
            if (FileName == null)
            {
                // TODO: merge with the other exception later.
                throw new FileNotFoundException(
                    string.Format(
                        "Filename: \\\\?\\{0}\r\nLine number: {1}\r\nError: Unrecognized configuration path 'MACHINE/WEBROOT/APPHOST/{2}'\r\n\r\n",
                        _server.FileName,
                        _lineNumber,
                        Location));
            }

            var file = this.FileName.ExpandIisExpressEnvironmentVariables();

            if (!File.Exists(file))
            {
                if (this.AppHost)
                {
                    _initialized = false;
                    throw new FileNotFoundException(
                        string.Format("Filename: \\\\?\\{0}\r\nError: Cannot read configuration file\r\n\r\n", this.FileName),
                        this.FileName);
                }

                return;
            }

            try
            {
                this.LoadDocument(file, Location);
            }
            catch (XmlException ex)
            {
                _initialized = false;
                throw new COMException(
                    string.Format(
                        "Filename: \\\\?\\{0}\r\nLine number: {1}\r\nError: Configuration file is not well-formed XML\r\n\r\n",
                        file,
                        ex.LineNumber));
            }
            catch (COMException ex)
            {
                _initialized = false;
                throw new COMException(string.Format("Filename: \\\\?\\{0}\r\n{1}\r\n", file, ex.Message));
            }
        }

        private void LoadSchemas()
        {
            _sectionSchemas.Clear();
            if (this.Parent == null)
            {
                this.LoadSchemasFromMode();
            }
            else
            {
                foreach (var item in this.Parent._sectionSchemas)
                {
                    _sectionSchemas.Add(item.Key, item.Value);
                }
            }
        }

        public void Save()
        {
            if (!_initialized || !_dirty)
            {
                return;
            }

            if (this.AppHost)
            {
                // TODO: load settings from applicationHost.config.
                var historyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Jexus Manager",
                    "history");
                int last = 0;
                if (!Directory.Exists(historyFolder))
                {
                    Directory.CreateDirectory(historyFolder);
                }
                else
                {
                    var existing = new DirectoryInfo(historyFolder).GetDirectories();
                    last =
                        existing.Select(found => found.Name.Substring("CFGHISTORY_".Length))
                            .Select(int.Parse)
                            .Concat(new[] { last })
                            .Max();
                }

                last++;
                var revisionFolder = Path.Combine(historyFolder, "CFGHISTORY_" + last.ToString("D10"));
                Directory.CreateDirectory(revisionFolder);
                var fileName = Path.GetFileName(this.FileName);
                if (fileName != null)
                {
                    File.Copy(this.FileName, Path.Combine(revisionFolder, fileName));
                }
            }

            _document?.Save(this.FileName);
        }

        public string FileName { get; set; }

        private static void CloneSectionGroups(SectionGroup source, SectionGroup destination)
        {
            foreach (var child in source.SectionGroups)
            {
                var newChild = destination.SectionGroups.Add(child.Name);
                CloneSectionGroups(child, newChild);
            }

            foreach (var define in source.Sections)
            {
                if (define.OverrideModeDefault == SectionGroup.KEYWORD_OVERRIDEMODE_ALLOW)
                {
                    destination.Sections.Add(define);
                }
#if DEBUG
#endif
            }
        }

        public FileContext Parent { get; }
        public string Location { get; }

        public SectionGroup GetEffectiveSectionGroup()
        {
            throw new NotImplementedException();
        }

        public string[] GetLocationPaths()
        {
            this.Initialize();
            return Locations.Select(item => item.Path).ToArray();
        }

        public object GetMetadata(string metadataType)
        {
            throw new NotImplementedException();
        }

        public ConfigurationSection GetSection(string sectionPath)
        {
            return GetSection(sectionPath, Location);
        }

        public ConfigurationSection GetSection(string sectionPath, string locationPath)
        {
            this.Initialize();
            if (!_dontThrow)
            {
                _server.VerifyLocation(locationPath);
            }

            return RootSectionGroup.FindSection(sectionPath, locationPath, this);
        }

        public ConfigurationSection GetSection(string sectionPath, Type type)
        {
            throw new NotImplementedException();
        }

        public ConfigurationSection GetSection(string sectionPath, Type type, string locationPath)
        {
            throw new NotImplementedException();
        }

        public void RemoveLocationPath(string locationPath)
        {
            this.Initialize();
            var location = Locations.FirstOrDefault(item => item.Path == locationPath);
            if (location != null)
            {
                Locations.Remove(location);
            }
        }

        public void RenameLocationPath(string locationPath, string newLocationPath)
        {
            this.Initialize();
            var location = Locations.FirstOrDefault(item => item.Path == locationPath);
            if (location != null)
            {
                location.Path = newLocationPath;
            }
        }

        public void SetMetadata(string metadataType, object value)
        {
            throw new NotImplementedException();
        }

        public SectionGroup RootSectionGroup
        {
            get
            {
                this.Initialize();
                return _rootSectionGroup;
            }
            set
            {
                _rootSectionGroup = value;
            }
        }

        private List<Location> Locations { get; }

        private readonly Dictionary<string, SectionSchema> _sectionSchemas = new Dictionary<string, SectionSchema>();
        private XDocument _document;

        private bool _initialized;

        private SectionGroup _rootSectionGroup;

        private bool _dirty;
        private int _lineNumber;

        private XElement Root
        {
            get { return _document?.Root; }
        }

        public ProtectedConfiguration ProtectedConfiguration
        {
            get
            {
                this.Initialize();
                return _protectedConfiguration
                       ?? (_protectedConfiguration =
                           new ProtectedConfiguration(this.GetSection("configProtectedData")));
            }
        }

        private ConfigurationElementSchema FindSchema(string path)
        {
            return _sectionSchemas.Values.Select(section => section.Root.FindSchema(path)).FirstOrDefault(result => result != null);
        }

        private SectionSchema FindSectionSchema(string path)
        {
            SectionSchema result;
            _sectionSchemas.TryGetValue(path, out result);
            return result;
        }

        private void LoadSchemasFromMode()
        {
            foreach (var file in _server.GetSchemaFiles())
            {
                var schemaDoc = XDocument.Load(file);
                LoadSchema(schemaDoc);
            }
        }

        private void LoadSchema(XDocument document)
        {
            if (document.Root == null)
            {
                return;
            }

            var nodes = document.Root.Nodes();
            foreach (var node in nodes)
            {
                if (node is XComment)
                {
                    continue;
                }

                var element = node as XElement;
                if (element == null)
                {
                    continue;
                }

                if (element.Name.LocalName != "sectionSchema")
                {
                    continue;
                }

                var name = element.Attribute("name").Value;
                var found = this.FindSectionSchema(name);
                if (found == null)
                {
                    found = new SectionSchema(name, element);
                    _sectionSchemas.Add(name, found);
                }

                found.ParseSectionSchema(element, null);
            }
        }

        private bool ParseSections(XElement element, ConfigurationElement parent, Location location)
        {
            var result = false;
            var path = element.GetPath();
            ConfigurationElement node = null;
            var schema = FindSchema(path);
            var sec = FindSectionSchema(path);
            var name = element.Name.LocalName;
            if (schema != null)
            {
                if (sec != null)
                {
                    var section = new ConfigurationSection(path, sec.Root, location?.Path, this, element);
                    this.RootSectionGroup.Add(section);
                    node = section;
                }
                else
                {
                    node = schema.CollectionSchema == null
                               ? new ConfigurationElement(null, name, schema, parent, element, this)
                               : new ConfigurationElementCollection(
                                     name,
                                     schema,
                                     parent,
                                     element,
                                     this);
                }

                parent?.AddChild(node);
                result = true;
            }

            foreach (var item in element.Nodes())
            {
                var child = item as XElement;
                if (child == null)
                {
                    continue;
                }

                var childAdded = ParseSections(child, node, location);
                result = result || childAdded;
            }

            if (!result && !_dontThrow && name != "location")
            {
                throw new COMException(
                    string.Format(
                        "Line number: {0}\r\nError: Unrecognized element '{1}'\r\n",
                        (element as IXmlLineInfo).LineNumber,
                        name));
            }

            return result;
        }

        private void LoadDocument(string file, string location)
        {
            _document = XDocument.Load(file, LoadOptions.SetLineInfo);
            if (Root == null)
            {
                return;
            }

            var nodes = Root.Nodes();
            foreach (var node in nodes)
            {
                if (node is XComment)
                {
                    continue;
                }

                var element = node as XElement;
                if (element != null)
                {
                    var tag = element.Name.LocalName;
                    if (tag == "configSections")
                    {
                        this.RootSectionGroup.ParseSectionDefinitions(element, _sectionSchemas);
                        continue;
                    }

                    if (tag == "location")
                    {
                        // IMPORTANT: allow null path due to root web.config.
                        var path = element.Attribute("path")?.Value;
                        var mode = element.Attribute("overrideMode").LoadString("Inherit");
                        var found = this.Locations.FirstOrDefault(item => item.Path == path);
                        if (found == null)
                        {
                            found = new Location(path, mode, element);
                            this.Locations.Add(found);
                        }

                        this.ParseSections(element, null, found);
                        continue;
                    }

                    if (location == null)
                    {
                        this.ParseSections(element, null, null);
                    }
                    else
                    {
                        var found = this.Locations.FirstOrDefault(item => item.Path == location);
                        if (found == null)
                        {
                            found = new Location(location, "Inherit", element);
                            this.Locations.Add(found);
                        }

                        this.ParseSections(element, null, found);
                    }
                }
            }
        }

        private void CloneParentSectionGroups(FileContext level)
        {
            if (level == null)
            {
                return;
            }

            if (level.RootSectionGroup != null)
            {
                CloneSectionGroups(level.RootSectionGroup, RootSectionGroup);
            }
            else
            {
                CloneParentSectionGroups(level.Parent);
            }
        }

        internal XElement CreateElement(string sectionPath, string locationPath)
        {
            var parts = sectionPath.Split('/');
            var top = EnsureDocumentExists();
            if (locationPath != null && locationPath != Location)
            {
                var elements = Root.XPathSelectElements(String.Format("//location[@path='{0}']", locationPath)).ToList();
                if (!elements.Any())
                {
                    var location = new XElement("location");
                    location.SetAttributeValue("path", locationPath);
                    top.Add(location);
                    top = location;
                }
                else
                {
                    top = elements.First();
                }
            }

            foreach (var part in parts)
            {
                var current = top.Element(part);
                if (current == null)
                {
                    current = new XElement(part);
                    top.Add(current);
                }

                top = current;
            }

            return top;
        }

        private XElement EnsureDocumentExists()
        {
            if (Root != null)
            {
                return Root;
            }

            var file = FileName.ExpandIisExpressEnvironmentVariables();
            var directory = Path.GetDirectoryName(file);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(file, "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration></configuration>");
            _document = XDocument.Load(file);
            return Root;
        }

        public bool Equals(FileContext other)
        {
            return other != null && other.FileName == this.FileName;
        }

        internal void SetDirty()
        {
            if (!_initialized)
            {
                return;
            }

            if (!_server.Initialized)
            {
                return;
            }

            if (this.ReadOnly)
            {
                throw new InvalidOperationException("The configuration object is read only, because it has been committed by a call to ServerManager.CommitChanges(). If write access is required, use ServerManager to get a new reference.");
            }

            _dirty = true;
        }
    }
}
