// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Document.h and
// Document.cpp.

using System;
using System.Collections.Generic;
using System.Text;

namespace MaterialX
{
    // Document Definition
    public partial class Document : GraphElement
    {
        // Document Public Methods
        public Document(Element parent, string name) : base(parent, CATEGORY, name)
        {
            _cache = new Cache();
        }

        public static Document createDocument()
        {
            Document doc = new Document(null, Mx.EMPTY_STRING);
            doc.initialize();
            return doc;
        }

        public virtual void initialize()
        {
            _root = getSelf();
            _cache.setDocument(getDocument());

            clearContent();
            setVersionIntegers(Mx.MATERIALX_MAJOR_VERSION, Mx.MATERIALX_MINOR_VERSION);
        }

        public virtual Document copy()
        {
            Document doc = createDocument();
            doc.copyContentFrom(getSelf());
            doc.setDataLibrary(getDataLibrary());
            return doc;
        }

        public StringSet getReferencedSourceUris()
        {
            StringSet sourceUris = new StringSet();
            foreach (Element elem in traverseTree())
            {
                if (elem.hasSourceUri())
                {
                    sourceUris.Add(elem.getSourceUri());
                }
            }
            return sourceUris;
        }

        public void setDataLibrary(Document dataLibrary)
        {
            _dataLibrary = dataLibrary;
        }

        public bool hasDataLibrary()
        {
            return _dataLibrary != null;
        }

        public Document getDataLibrary()
        {
            return _dataLibrary;
        }

        public void importLibrary(Document library)
        {
            if (library == null)
            {
                return;
            }

            foreach (Element child in new List<Element>(library.getChildren()))
            {
                if (child.getCategory().Length == 0)
                {
                    throw new Exception("Trying to import child without a category: " + child.getName());
                }

                string childName = child.getQualifiedName(child.getName());

                // Check for duplicate elements.
                Element previous = getChild(childName);
                if (previous != null)
                {
                    continue;
                }

                // Create the imported element.
                Element childCopy = addChildOfCategory(child.getCategory(), childName);
                childCopy.copyContentFrom(child);
                if (!childCopy.hasFilePrefix() && library.hasFilePrefix())
                {
                    childCopy.setFilePrefix(library.getFilePrefix());
                }
                if (!childCopy.hasGeomPrefix() && library.hasGeomPrefix())
                {
                    childCopy.setGeomPrefix(library.getGeomPrefix());
                }
                if (!childCopy.hasColorSpace() && library.hasColorSpace())
                {
                    childCopy.setColorSpace(library.getColorSpace());
                }
                if (!childCopy.hasNamespace() && library.hasNamespace())
                {
                    childCopy.setNamespace(library.getNamespace());
                }
                if (!childCopy.hasSourceUri() && library.hasSourceUri())
                {
                    childCopy.setSourceUri(library.getSourceUri());
                }
            }
        }

        public NodeGraph addNodeGraph(string name = Mx.EMPTY_STRING)
        {
            return addChild<NodeGraph>(name);
        }

        public NodeGraph getNodeGraph(string name)
        {
            return getChildOfType<NodeGraph>(name);
        }

        public List<NodeGraph> getNodeGraphs()
        {
            return getChildrenOfType<NodeGraph>();
        }

        public void removeNodeGraph(string name)
        {
            removeChildOfType<NodeGraph>(name);
        }

        public List<PortElement> getMatchingPorts(string nodeName)
        {
            return _cache.getMatchingPorts(nodeName);
        }

        public GeomInfo addGeomInfo(string name = Mx.EMPTY_STRING, string geom = Mx.UNIVERSAL_GEOM_NAME)
        {
            GeomInfo geomInfo = addChild<GeomInfo>(name);
            geomInfo.setGeom(geom);
            return geomInfo;
        }

        public GeomInfo getGeomInfo(string name)
        {
            return getChildOfType<GeomInfo>(name);
        }

        public List<GeomInfo> getGeomInfos()
        {
            return getChildrenOfType<GeomInfo>();
        }

        public void removeGeomInfo(string name)
        {
            removeChildOfType<GeomInfo>(name);
        }

        public Value getGeomPropValue(string geomPropName, string geom = Mx.UNIVERSAL_GEOM_NAME)
        {
            Value value = null;
            foreach (GeomInfo geomInfo in getGeomInfos())
            {
                if (!Mx.geomStringsMatch(geom, geomInfo.getActiveGeom()))
                {
                    continue;
                }
                GeomProp geomProp = geomInfo.getGeomProp(geomPropName);
                if (geomProp != null)
                {
                    value = geomProp.getValue();
                }
            }
            return value;
        }

        public GeomPropDef addGeomPropDef(string name, string geomprop)
        {
            GeomPropDef geomPropDef = addChild<GeomPropDef>(name);
            geomPropDef.setGeomProp(geomprop);
            return geomPropDef;
        }

        public GeomPropDef getGeomPropDef(string name)
        {
            return getChildOfType<GeomPropDef>(name);
        }

        public List<GeomPropDef> getGeomPropDefs()
        {
            return getChildrenOfType<GeomPropDef>();
        }

        public void removeGeomPropDef(string name)
        {
            removeChildOfType<GeomPropDef>(name);
        }

        public List<Output> getMaterialOutputs()
        {
            List<Output> materialOutputs = new List<Output>();

            string documentUri = getSourceUri();
            foreach (NodeGraph docNodeGraph in getNodeGraphs())
            {
                // Skip nodegraphs which are either definitions or are from an included file.
                string graphUri = docNodeGraph.getSourceUri();
                if (docNodeGraph.getNodeDef() != null || (graphUri.Length != 0 && documentUri != graphUri))
                {
                    continue;
                }

                List<Output> docNodeGraphOutputs = docNodeGraph.getMaterialOutputs();
                if (docNodeGraphOutputs.Count != 0)
                {
                    materialOutputs.AddRange(docNodeGraphOutputs);
                }
            }
            return materialOutputs;
        }

        public Look addLook(string name = Mx.EMPTY_STRING)
        {
            return addChild<Look>(name);
        }

        public Look getLook(string name)
        {
            return getChildOfType<Look>(name);
        }

        public List<Look> getLooks()
        {
            return getChildrenOfType<Look>();
        }

        public void removeLook(string name)
        {
            removeChildOfType<Look>(name);
        }

        public LookGroup addLookGroup(string name = Mx.EMPTY_STRING)
        {
            return addChild<LookGroup>(name);
        }

        public LookGroup getLookGroup(string name)
        {
            return getChildOfType<LookGroup>(name);
        }

        public List<LookGroup> getLookGroups()
        {
            return getChildrenOfType<LookGroup>();
        }

        public void removeLookGroup(string name)
        {
            removeChildOfType<LookGroup>(name);
        }

        public Collection addCollection(string name = Mx.EMPTY_STRING)
        {
            return addChild<Collection>(name);
        }

        public Collection getCollection(string name)
        {
            return getChildOfType<Collection>(name);
        }

        public List<Collection> getCollections()
        {
            return getChildrenOfType<Collection>();
        }

        public void removeCollection(string name)
        {
            removeChildOfType<Collection>(name);
        }

        public TypeDef addTypeDef(string name)
        {
            return addChild<TypeDef>(name);
        }

        public TypeDef getTypeDef(string name)
        {
            return getChildOfType<TypeDef>(name);
        }

        public List<TypeDef> getTypeDefs()
        {
            return getChildrenOfType<TypeDef>();
        }

        public void removeTypeDef(string name)
        {
            removeChildOfType<TypeDef>(name);
        }

        public NodeDef addNodeDef(string name = Mx.EMPTY_STRING, string type = Mx.DEFAULT_TYPE_STRING, string node = Mx.EMPTY_STRING)
        {
            NodeDef child = addChild<NodeDef>(name);
            if (type.Length != 0 && type != Mx.MULTI_OUTPUT_TYPE_STRING)
            {
                child.addOutput("out", type);
            }
            if (node.Length != 0)
            {
                child.setNodeString(node);
            }
            return child;
        }

        public NodeDef addNodeDefFromGraph(NodeGraph nodeGraph, string nodeDefName, string category, string newGraphName)
        {
            if (category.Length == 0)
            {
                throw new Exception("Cannot create a nodedef without a category identifier");
            }
            if (getNodeDef(nodeDefName) != null)
            {
                throw new Exception("Cannot create duplicate nodedef: " + nodeDefName);
            }
            if (getNodeGraph(newGraphName) != null)
            {
                throw new Exception("Cannot create duplicate nodegraph: " + newGraphName);
            }

            // Create a new functional nodegraph, and transfer over the contents from the compound nodegraph
            NodeGraph graph = addNodeGraph(newGraphName);
            graph.copyContentFrom(nodeGraph);

            foreach (Element graphChild in graph.getChildren())
            {
                graphChild.removeAttribute(Element.XPOS_ATTRIBUTE);
                graphChild.removeAttribute(Element.YPOS_ATTRIBUTE);
            }
            graph.setNodeDefString(nodeDefName);

            // Create a new nodedef and set its category
            NodeDef nodeDef = addNodeDef(nodeDefName, Mx.EMPTY_STRING);
            nodeDef.setNodeString(category);

            // Expose any existing interfaces from the graph.
            // Any connection attributes ("nodegraph", "nodename", "interfacename") on the
            // existing interface should be removed from the definition as well as any source URI.

            // Attributes which should not be copied over
            StringSet filterAttributes = new StringSet(new string[] { PortElement.NODE_GRAPH_ATTRIBUTE, PortElement.NODE_NAME_ATTRIBUTE,
                                                                      PortElement.INTERFACE_NAME_ATTRIBUTE, Element.XPOS_ATTRIBUTE, Element.YPOS_ATTRIBUTE });

            // Transfer input interface from the graph to the nodedef
            foreach (Input input in graph.getInputs())
            {
                Input nodeDefInput = nodeDef.addInput(input.getName(), input.getType());
                if (nodeDefInput != null)
                {
                    nodeDefInput.copyContentFrom(input);
                    foreach (string filterAttribute in filterAttributes)
                    {
                        nodeDefInput.removeAttribute(filterAttribute);
                    }
                    nodeDefInput.setSourceUri(Mx.EMPTY_STRING);
                    input.setInterfaceName(nodeDefInput.getName());
                }
            }
            // Remove interfaces from the nodegraph
            foreach (Input input in graph.getInputs())
            {
                graph.removeInput(input.getName());
            }

            // Copy the output interface from the graph to the nodedef
            foreach (Output output in graph.getOutputs())
            {
                Output nodeDefOutput = nodeDef.addOutput(output.getName(), output.getType());
                if (nodeDefOutput != null)
                {
                    nodeDefOutput.copyContentFrom(output);
                    foreach (string filterAttribute in filterAttributes)
                    {
                        nodeDefOutput.removeAttribute(filterAttribute);
                    }
                    nodeDefOutput.setSourceUri(Mx.EMPTY_STRING);
                }
            }

            return nodeDef;
        }

        public NodeDef getNodeDef(string name)
        {
            return getChildOfType<NodeDef>(name);
        }

        public List<NodeDef> getNodeDefs()
        {
            return getChildrenOfType<NodeDef>();
        }

        public void removeNodeDef(string name)
        {
            removeChildOfType<NodeDef>(name);
        }

        public List<NodeDef> getMatchingNodeDefs(string nodeName)
        {
            // Recurse to data library if present.
            List<NodeDef> matchingNodeDefs = hasDataLibrary() ?
                                             getDataLibrary().getMatchingNodeDefs(nodeName) :
                                             new List<NodeDef>();

            // Append all nodedefs matching the given node name.
            List<NodeDef> localNodeDefs = _cache.getMatchingNodeDefs(nodeName);
            matchingNodeDefs.AddRange(localNodeDefs);

            return matchingNodeDefs;
        }

        public AttributeDef addAttributeDef(string name = Mx.EMPTY_STRING)
        {
            return addChild<AttributeDef>(name);
        }

        public AttributeDef getAttributeDef(string name)
        {
            return getChildOfType<AttributeDef>(name);
        }

        public List<AttributeDef> getAttributeDefs()
        {
            return getChildrenOfType<AttributeDef>();
        }

        public void removeAttributeDef(string name)
        {
            removeChildOfType<AttributeDef>(name);
        }

        public TargetDef addTargetDef(string name = Mx.EMPTY_STRING)
        {
            return addChild<TargetDef>(name);
        }

        public TargetDef getTargetDef(string name)
        {
            return getChildOfType<TargetDef>(name);
        }

        public List<TargetDef> getTargetDefs()
        {
            return getChildrenOfType<TargetDef>();
        }

        public void removeTargetDef(string name)
        {
            removeChildOfType<TargetDef>(name);
        }

        public PropertySet addPropertySet(string name = Mx.EMPTY_STRING)
        {
            return addChild<PropertySet>(name);
        }

        public PropertySet getPropertySet(string name)
        {
            return getChildOfType<PropertySet>(name);
        }

        public List<PropertySet> getPropertySets()
        {
            return getChildrenOfType<PropertySet>();
        }

        public void removePropertySet(string name)
        {
            removeChildOfType<PropertySet>(name);
        }

        public VariantSet addVariantSet(string name = Mx.EMPTY_STRING)
        {
            return addChild<VariantSet>(name);
        }

        public VariantSet getVariantSet(string name)
        {
            return getChildOfType<VariantSet>(name);
        }

        public List<VariantSet> getVariantSets()
        {
            return getChildrenOfType<VariantSet>();
        }

        public void removeVariantSet(string name)
        {
            removeChildOfType<VariantSet>(name);
        }

        public Implementation addImplementation(string name = Mx.EMPTY_STRING)
        {
            return addChild<Implementation>(name);
        }

        public Implementation getImplementation(string name)
        {
            return getChildOfType<Implementation>(name);
        }

        public List<Implementation> getImplementations()
        {
            return getChildrenOfType<Implementation>();
        }

        public void removeImplementation(string name)
        {
            removeChildOfType<Implementation>(name);
        }

        public List<InterfaceElement> getMatchingImplementations(string nodeDef)
        {
            // Recurse to data library if present.
            List<InterfaceElement> matchingImplementations = hasDataLibrary() ?
                                                             getDataLibrary().getMatchingImplementations(nodeDef) :
                                                             new List<InterfaceElement>();

            // Append all implementations matching the given nodedef string.
            List<InterfaceElement> localImpls = _cache.getMatchingImplementations(nodeDef);
            matchingImplementations.AddRange(localImpls);

            return matchingImplementations;
        }

        public UnitDef addUnitDef(string name)
        {
            if (name.Length == 0)
            {
                throw new Exception("A unit definition name cannot be empty");
            }
            return addChild<UnitDef>(name);
        }

        public UnitDef getUnitDef(string name)
        {
            return getChildOfType<UnitDef>(name);
        }

        public List<UnitDef> getUnitDefs()
        {
            return getChildrenOfType<UnitDef>();
        }

        public void removeUnitDef(string name)
        {
            removeChildOfType<UnitDef>(name);
        }

        public UnitTypeDef addUnitTypeDef(string name)
        {
            if (name.Length == 0)
            {
                throw new Exception("A unit type definition name cannot be empty");
            }
            return addChild<UnitTypeDef>(name);
        }

        public UnitTypeDef getUnitTypeDef(string name)
        {
            return getChildOfType<UnitTypeDef>(name);
        }

        public List<UnitTypeDef> getUnitTypeDefs()
        {
            return getChildrenOfType<UnitTypeDef>();
        }

        public void removeUnitTypeDef(string name)
        {
            removeChildOfType<UnitTypeDef>(name);
        }

        public override (int, int) getVersionIntegers()
        {
            if (!hasVersionString())
            {
                return (Mx.MATERIALX_MAJOR_VERSION, Mx.MATERIALX_MINOR_VERSION);
            }
            return base.getVersionIntegers();
        }

        public void setColorManagementSystem(string cms)
        {
            setAttribute(CMS_ATTRIBUTE, cms);
        }

        public bool hasColorManagementSystem()
        {
            return hasAttribute(CMS_ATTRIBUTE);
        }

        public string getColorManagementSystem()
        {
            return getAttribute(CMS_ATTRIBUTE);
        }

        public void setColorManagementConfig(string cmsConfig)
        {
            setAttribute(CMS_CONFIG_ATTRIBUTE, cmsConfig);
        }

        public bool hasColorManagementConfig()
        {
            return hasAttribute(CMS_CONFIG_ATTRIBUTE);
        }

        public string getColorManagementConfig()
        {
            return getAttribute(CMS_CONFIG_ATTRIBUTE);
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            (int, int) expectedVersion = (Mx.MATERIALX_MAJOR_VERSION, Mx.MATERIALX_MINOR_VERSION);
            validateRequire(getVersionIntegers().CompareTo(expectedVersion) >= 0, ref res, message, "Unsupported document version");
            validateRequire(getVersionIntegers().CompareTo(expectedVersion) <= 0, ref res, message, "Future document version");
            return base.validate(message) && res;
        }

        public void invalidateCache()
        {
            _cache.invalidate();
        }

        // Document Public Members
        public const string CATEGORY = "materialx";
        public const string CMS_ATTRIBUTE = "cms";
        public const string CMS_CONFIG_ATTRIBUTE = "cmsconfig";

        // Document Private Types
        private class Cache
        {
            public Cache()
            {
                _valid = false;
            }

            public void setDocument(Document document)
            {
                lock (_mutex)
                {
                    _doc = document;
                    _valid = false;
                }
            }

            public void invalidate()
            {
                // A plain write; readers take the lock in refresh().
                _valid = false;
            }

            public List<PortElement> getMatchingPorts(string nodeName)
            {
                lock (_mutex)
                {
                    refresh();
                    List<PortElement> found;
                    return _portElementMap.TryGetValue(nodeName, out found) ? new List<PortElement>(found) : new List<PortElement>();
                }
            }

            public List<NodeDef> getMatchingNodeDefs(string nodeName)
            {
                lock (_mutex)
                {
                    refresh();
                    List<NodeDef> found;
                    return _nodeDefMap.TryGetValue(nodeName, out found) ? new List<NodeDef>(found) : new List<NodeDef>();
                }
            }

            public List<InterfaceElement> getMatchingImplementations(string nodeDef)
            {
                lock (_mutex)
                {
                    refresh();
                    List<InterfaceElement> found;
                    return _implementationMap.TryGetValue(nodeDef, out found) ? new List<InterfaceElement>(found) : new List<InterfaceElement>();
                }
            }

            private void refresh()
            {
                if (_valid)
                {
                    return;
                }
                Document doc = _doc;
                if (doc != null)
                {
                    rebuild(doc);
                }
            }

            private void rebuild(Document doc)
            {
                // Clear the existing cache.
                _portElementMap.Clear();
                _nodeDefMap.Clear();
                _implementationMap.Clear();

                // Traverse the document to build a new cache.
                foreach (Element elem in doc.traverseTree())
                {
                    string nodeName = elem.getAttribute(PortElement.NODE_NAME_ATTRIBUTE);
                    string nodeGraphName = elem.getAttribute(PortElement.NODE_GRAPH_ATTRIBUTE);
                    string nodeString = elem.getAttribute(NodeDef.NODE_ATTRIBUTE);
                    string nodeDefString = elem.getAttribute(InterfaceElement.NODE_DEF_ATTRIBUTE);

                    string portKey = nodeName.Length != 0 ? nodeName : nodeGraphName;
                    if (portKey.Length != 0)
                    {
                        PortElement portElem = elem.asA<PortElement>();
                        if (portElem != null)
                        {
                            string key = portElem.getQualifiedName(portKey);
                            List<PortElement> list;
                            if (!_portElementMap.TryGetValue(key, out list))
                            {
                                list = new List<PortElement>();
                                _portElementMap[key] = list;
                            }
                            list.Add(portElem);
                        }
                    }
                    if (nodeString.Length != 0)
                    {
                        NodeDef nodeDef = elem.asA<NodeDef>();
                        if (nodeDef != null)
                        {
                            string key = nodeDef.getQualifiedName(nodeString);
                            List<NodeDef> list;
                            if (!_nodeDefMap.TryGetValue(key, out list))
                            {
                                list = new List<NodeDef>();
                                _nodeDefMap[key] = list;
                            }
                            list.Add(nodeDef);
                        }
                    }
                    if (nodeDefString.Length != 0)
                    {
                        InterfaceElement interfaceElement = elem.asA<InterfaceElement>();
                        if (interfaceElement != null)
                        {
                            if (interfaceElement.isA<Implementation>() || interfaceElement.isA<NodeGraph>())
                            {
                                string key = interfaceElement.getQualifiedName(nodeDefString);
                                List<InterfaceElement> list;
                                if (!_implementationMap.TryGetValue(key, out list))
                                {
                                    list = new List<InterfaceElement>();
                                    _implementationMap[key] = list;
                                }
                                list.Add(interfaceElement);
                            }
                        }
                    }
                }

                _valid = true;
            }

            private Document _doc;
            private readonly object _mutex = new object();
            private volatile bool _valid;
            private Dictionary<string, List<PortElement>> _portElementMap = new Dictionary<string, List<PortElement>>(StringComparer.Ordinal);
            private Dictionary<string, List<NodeDef>> _nodeDefMap = new Dictionary<string, List<NodeDef>>(StringComparer.Ordinal);
            private Dictionary<string, List<InterfaceElement>> _implementationMap = new Dictionary<string, List<InterfaceElement>>(StringComparer.Ordinal);
        }

        // Document Private Members
        private Document _dataLibrary;
        private Cache _cache;
    }

    // Document Functions
    public static partial class Mx
    {
        public static Document createDocument()
        {
            return Document.createDocument();
        }
    }
}
