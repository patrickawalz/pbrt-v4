// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Element.h and
// Element.cpp.

using System;
using System.Collections.Generic;
using System.Text;

namespace MaterialX
{
    // Element Definition
    public abstract class Element
    {
        // Element Protected Methods
        protected Element(Element parent, string category, string name)
        {
            _category = category;
            _name = name;
            _parent = parent;
            _root = parent != null ? parent.getRoot() : null;
        }

        // Element Public Methods

        // Return true if the given element tree, including all descendants, is
        // identical to this one (operator== in the C++; references to elements
        // are compared by identity throughout this port).
        public bool isEqual(Element rhs)
        {
            if (getCategory() != rhs.getCategory() ||
                getName() != rhs.getName())
            {
                return false;
            }

            // Compare attribute names and values.
            List<string> names = getAttributeNames();
            List<string> rhsNames = rhs.getAttributeNames();
            if (names.Count != rhsNames.Count)
            {
                return false;
            }
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] != rhsNames[i])
                {
                    return false;
                }
            }
            foreach (string attr in rhs.getAttributeNames())
            {
                if (getAttribute(attr) != rhs.getAttribute(attr))
                {
                    return false;
                }
            }

            // Compare children.
            List<Element> c1 = getChildren();
            List<Element> c2 = rhs.getChildren();
            if (c1.Count != c2.Count)
            {
                return false;
            }
            for (int i = 0; i < c1.Count; i++)
            {
                if (!c1[i].isEqual(c2[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public void setCategory(string category)
        {
            _category = category;
        }

        public string getCategory()
        {
            return _category;
        }

        public void setName(string name)
        {
            Element parent = getParent();
            if (parent != null && parent._childMap.ContainsKey(name) && name != getName())
            {
                throw new Exception("Element name is not unique at the given scope: " + name);
            }

            getDocument().invalidateCache();

            if (parent != null)
            {
                parent._childMap.Remove(getName());
                parent._childMap[name] = getSelf();
            }
            _name = name;
        }

        public string getName()
        {
            return _name;
        }

        public string getNamePath(Element relativeTo = null)
        {
            if (relativeTo == null)
            {
                relativeTo = getDocument();
            }

            string res = "";
            for (Element elem = getSelf(); elem != null; elem = elem.getParent())
            {
                if (elem == relativeTo)
                {
                    break;
                }
                res = res.Length == 0 ? elem.getName() : elem.getName() + Mx.NAME_PATH_SEPARATOR + res;
            }
            return res;
        }

        public Element getDescendant(string namePath)
        {
            List<string> nameVec = Mx.splitString(namePath, Mx.NAME_PATH_SEPARATOR);
            Element elem = getSelfNonConst();
            foreach (string name in nameVec)
            {
                elem = elem.getChild(name);
                if (elem == null)
                {
                    return null;
                }
            }
            return elem;
        }

        public void setFilePrefix(string prefix)
        {
            setAttribute(FILE_PREFIX_ATTRIBUTE, prefix);
        }

        public bool hasFilePrefix()
        {
            return hasAttribute(FILE_PREFIX_ATTRIBUTE);
        }

        public string getFilePrefix()
        {
            return getAttribute(FILE_PREFIX_ATTRIBUTE);
        }

        public string getActiveFilePrefix()
        {
            for (Element elem = getSelf(); elem != null; elem = elem.getParent())
            {
                if (elem.hasFilePrefix())
                {
                    return elem.getFilePrefix();
                }
            }
            return Mx.EMPTY_STRING;
        }

        public void setGeomPrefix(string prefix)
        {
            setAttribute(GEOM_PREFIX_ATTRIBUTE, prefix);
        }

        public bool hasGeomPrefix()
        {
            return hasAttribute(GEOM_PREFIX_ATTRIBUTE);
        }

        public string getGeomPrefix()
        {
            return getAttribute(GEOM_PREFIX_ATTRIBUTE);
        }

        public string getActiveGeomPrefix()
        {
            for (Element elem = getSelf(); elem != null; elem = elem.getParent())
            {
                if (elem.hasGeomPrefix())
                {
                    return elem.getGeomPrefix();
                }
            }
            return Mx.EMPTY_STRING;
        }

        public void setColorSpace(string colorSpace)
        {
            setAttribute(COLOR_SPACE_ATTRIBUTE, colorSpace);
        }

        public bool hasColorSpace()
        {
            return hasAttribute(COLOR_SPACE_ATTRIBUTE);
        }

        public string getColorSpace()
        {
            return getAttribute(COLOR_SPACE_ATTRIBUTE);
        }

        public string getActiveColorSpace()
        {
            for (Element elem = getSelf(); elem != null; elem = elem.getParent())
            {
                if (elem.hasColorSpace())
                {
                    return elem.getColorSpace();
                }
            }
            return Mx.EMPTY_STRING;
        }

        public void setInheritString(string inherit)
        {
            setAttribute(INHERIT_ATTRIBUTE, inherit);
        }

        public bool hasInheritString()
        {
            return hasAttribute(INHERIT_ATTRIBUTE);
        }

        public string getInheritString()
        {
            return getAttribute(INHERIT_ATTRIBUTE);
        }

        public void setInheritsFrom(Element super)
        {
            if (super != null)
            {
                setInheritString(super.getName());
            }
            else
            {
                removeAttribute(INHERIT_ATTRIBUTE);
            }
        }

        public Element getInheritsFrom()
        {
            return hasInheritString() ? resolveNameReference<Element>(getInheritString()) : null;
        }

        public bool hasInheritedBase(Element baseElement)
        {
            foreach (Element elem in traverseInheritance())
            {
                if (elem == baseElement)
                {
                    return true;
                }
            }
            return false;
        }

        public bool hasInheritanceCycle()
        {
            // Skip the traversal when no inheritance is set.
            if (!hasInheritString())
            {
                return false;
            }
            try
            {
                foreach (Element elem in traverseInheritance())
                {
                }
            }
            catch (ExceptionFoundCycle)
            {
                return true;
            }
            return false;
        }

        public void setNamespace(string space)
        {
            setAttribute(NAMESPACE_ATTRIBUTE, space);
        }

        public bool hasNamespace()
        {
            return hasAttribute(NAMESPACE_ATTRIBUTE);
        }

        public string getNamespace()
        {
            return getAttribute(NAMESPACE_ATTRIBUTE);
        }

        public string getQualifiedName(string name)
        {
            for (Element elem = getSelf(); elem != null; elem = elem.getParent())
            {
                string namespaceStr = elem.getNamespace();
                if (namespaceStr.Length != 0)
                {
                    // Check if the name is qualified already.
                    int i = name.IndexOf(Mx.NAME_PREFIX_SEPARATOR, StringComparison.Ordinal);
                    if (i != -1 && name.Substring(0, i) == namespaceStr)
                    {
                        return name;
                    }
                    return namespaceStr + Mx.NAME_PREFIX_SEPARATOR + name;
                }
            }
            return name;
        }

        public void setDocString(string doc)
        {
            setAttribute(DOC_ATTRIBUTE, doc);
        }

        public string getDocString()
        {
            return getAttribute(DOC_ATTRIBUTE);
        }

        public bool isA<T>(string category = Mx.EMPTY_STRING) where T : Element
        {
            if (!(this is T))
            {
                return false;
            }
            if (category.Length != 0 && getCategory() != category)
            {
                return false;
            }
            return true;
        }

        public T asA<T>() where T : Element
        {
            return this as T;
        }

        public T addChild<T>(string name = Mx.EMPTY_STRING) where T : Element
        {
            string category = categoryOf<T>();
            string childName = name;
            if (childName.Length == 0)
            {
                childName = createValidChildName(category + "1");
            }

            if (_childMap.ContainsKey(childName))
            {
                throw new Exception("Child name is not unique: " + childName);
            }

            T child = (T)_creatorMap[category](getSelf(), childName);
            registerChildElement(child);

            return child;
        }

        public Element addChildOfCategory(string category, string name = Mx.EMPTY_STRING)
        {
            if (name.Length == 0)
            {
                name = createValidChildName(category + "1");
            }

            if (_childMap.ContainsKey(name))
            {
                throw new Exception("Child name is not unique: " + name);
            }

            Element child = null;

            // Check for this category in the creator map.
            Func<Element, string, Element> creator;
            if (_creatorMap.TryGetValue(category, out creator))
            {
                child = creator(getSelf(), name);
            }

            // Check for a node within a graph.
            if (child == null && isA<GraphElement>())
            {
                child = new Node(getSelf(), name);
                child.setCategory(category);
            }

            // If no match was found, then create a generic element.
            if (child == null)
            {
                child = new GenericElement(getSelf(), name);
                child.setCategory(category);
            }

            registerChildElement(child);

            return child;
        }

        public Element changeChildCategory(Element child, string category)
        {
            int childIndex = getChildIndex(child.getName());
            if (childIndex == -1)
            {
                return null;
            }

            removeChild(child.getName());
            Element newChild = addChildOfCategory(category, child.getName());
            setChildIndex(child.getName(), childIndex);
            newChild.copyContentFrom(child);
            return newChild;
        }

        public Element getChild(string name)
        {
            Element found;
            if (_childMap.TryGetValue(name, out found))
            {
                return found;
            }
            Document doc = asA<Document>();
            if (doc != null && doc.hasDataLibrary())
            {
                return doc.getDataLibrary().getChild(name);
            }
            return null;
        }

        public T getChildOfType<T>(string name) where T : Element
        {
            Element child = null;
            Document doc = asA<Document>();
            if (doc != null && doc.hasDataLibrary())
            {
                child = doc.getDataLibrary().getChild(name);
            }
            if (child == null)
            {
                child = getChild(name);
            }
            return child != null ? child.asA<T>() : null;
        }

        public List<Element> getChildren()
        {
            return _childOrder;
        }

        public List<T> getChildrenOfType<T>(string category = Mx.EMPTY_STRING) where T : Element
        {
            List<T> children;
            Document doc = asA<Document>();
            if (doc != null && doc.hasDataLibrary())
            {
                children = doc.getDataLibrary().getChildrenOfType<T>(category);
            }
            else
            {
                children = new List<T>();
            }
            foreach (Element child in _childOrder)
            {
                T instance = child as T;
                if (instance == null)
                {
                    continue;
                }
                if (category.Length != 0 && child.getCategory() != category)
                {
                    continue;
                }
                children.Add(instance);
            }
            return children;
        }

        public void setChildIndex(string name, int index)
        {
            Element child = getChild(name);
            int it = indexOfChild(child);
            if (it == -1)
            {
                return;
            }

            if (index < 0 || index >= _childOrder.Count)
            {
                throw new Exception("Invalid child index");
            }

            _childOrder.RemoveAt(it);
            _childOrder.Insert(index, child);
        }

        public int getChildIndex(string name)
        {
            Element child = getChild(name);
            return indexOfChild(child);
        }

        public void removeChild(string name)
        {
            Element found;
            if (!_childMap.TryGetValue(name, out found))
            {
                return;
            }

            unregisterChildElement(found);
        }

        public void removeChildOfType<T>(string name) where T : Element
        {
            if (getChildOfType<T>(name) != null)
            {
                removeChild(name);
            }
        }

        public void setAttribute(string attrib, string value)
        {
            getDocument().invalidateCache();

            if (!_attributeMap.ContainsKey(attrib))
            {
                _attributeOrder.Add(attrib);
            }
            _attributeMap[attrib] = value;
        }

        public bool hasAttribute(string attrib)
        {
            return _attributeMap.ContainsKey(attrib);
        }

        public string getAttribute(string attrib)
        {
            string value;
            return _attributeMap.TryGetValue(attrib, out value) ? value : Mx.EMPTY_STRING;
        }

        public List<string> getAttributeNames()
        {
            return _attributeOrder;
        }

        public void setTypedAttribute<T>(string attrib, T data)
        {
            setAttribute(attrib, Mx.toValueString<T>(data));
        }

        public T getTypedAttribute<T>(string attrib)
        {
            if (hasAttribute(attrib))
            {
                try
                {
                    return Mx.fromValueString<T>(getAttribute(attrib));
                }
                catch (ExceptionTypeError)
                {
                }
            }
            return Mx.defaultValue<T>();
        }

        public void removeAttribute(string attrib)
        {
            if (_attributeMap.ContainsKey(attrib))
            {
                getDocument().invalidateCache();

                _attributeMap.Remove(attrib);
                _attributeOrder.Remove(attrib);
            }
        }

        public Element getSelf()
        {
            return this;
        }

        public Element getParent()
        {
            return _parent;
        }

        public Element getRoot()
        {
            Element root = _root;
            if (root == null)
            {
                throw new ExceptionOrphanedElement("Requested root of orphaned element: " + asString());
            }
            return root;
        }

        public Document getDocument()
        {
            return getRoot().asA<Document>();
        }

        public T getAncestorOfType<T>() where T : Element
        {
            for (Element elem = getSelf(); elem != null; elem = elem.getParent())
            {
                T typedElem = elem.asA<T>();
                if (typedElem != null)
                {
                    return typedElem;
                }
            }
            return null;
        }

        public bool isEquivalent(Element rhs, ElementEquivalenceOptions options, StringBuilder message = null)
        {
            if (getName() != rhs.getName())
            {
                if (message != null)
                {
                    message.Append("Name of " + getNamePath() + " differs from " + rhs.getNamePath() + "\n");
                }
                return false;
            }
            if (getCategory() != rhs.getCategory())
            {
                if (message != null)
                {
                    message.Append("Category of " + getNamePath() + " differs from " + rhs.getNamePath() + "\n");
                }
                return false;
            }

            // Compare attribute names.
            List<string> attributeNames = new List<string>(getAttributeNames());
            List<string> rhsAttributeNames = new List<string>(rhs.getAttributeNames());

            // Filter out any attributes specified in the options.
            StringSet attributeExclusionList = options.attributeExclusionList;
            if (attributeExclusionList.Count != 0)
            {
                attributeNames.RemoveAll((string attr) => attributeExclusionList.Contains(attr));
                rhsAttributeNames.RemoveAll((string attr) => attributeExclusionList.Contains(attr));
            }

            // Ignore attribute ordering by sorting names
            attributeNames.Sort(StringComparer.Ordinal);
            rhsAttributeNames.Sort(StringComparer.Ordinal);

            if (!System.Linq.Enumerable.SequenceEqual(attributeNames, rhsAttributeNames))
            {
                if (message != null)
                {
                    message.Append("Attribute names of '" + getNamePath() + "' differ from '" + rhs.getNamePath() + "\n");
                }
                return false;
            }

            foreach (string attr in rhsAttributeNames)
            {
                if (!isAttributeEquivalent(rhs, attr, options, message))
                {
                    return false;
                }
            }

            // Compare all child elements that affect functional equivalence.
            List<Element> children = new List<Element>();
            foreach (Element child in getChildren())
            {
                if (child.getCategory() == CommentElement.CATEGORY)
                {
                    continue;
                }
                children.Add(child);
            }
            List<Element> rhsChildren = new List<Element>();
            foreach (Element child in rhs.getChildren())
            {
                if (child.getCategory() == CommentElement.CATEGORY)
                {
                    continue;
                }
                rhsChildren.Add(child);
            }
            if (children.Count != rhsChildren.Count)
            {
                if (message != null)
                {
                    message.Append("Child count of " + getNamePath() + " differs from " + rhs.getNamePath() + "\n");
                }
                return false;
            }
            for (int i = 0; i < children.Count; i++)
            {
                Element rhsElement = rhsChildren[i];
                // Skip child order matching for document and graph elements.
                GraphElement graph = this.getSelf().asA<GraphElement>();
                if (graph != null)
                {
                    NodeGraph nodeGraph = graph.asA<NodeGraph>();
                    Document document = graph.asA<Document>();
                    if (document != null || (nodeGraph != null && nodeGraph.getNodeDef() == null))
                    {
                        string childName = children[i].getName();
                        rhsElement = rhs.getChild(childName);
                        if (rhsElement == null)
                        {
                            if (message != null)
                            {
                                message.Append("Child name `" + childName + "` of " + getNamePath() + " differs from " + rhs.getNamePath() + "\n");
                            }
                            return false;
                        }
                    }
                }
                if (!children[i].isEquivalent(rhsElement, options, message))
                {
                    return false;
                }
            }
            return true;
        }

        public virtual bool isAttributeEquivalent(Element rhs, string attributeName,
                                                  ElementEquivalenceOptions options, StringBuilder message = null)
        {
            if (getAttribute(attributeName) != rhs.getAttribute(attributeName))
            {
                if (message != null)
                {
                    message.Append("Attribute name `" + attributeName + "` of " + getNamePath() + " differs from " + rhs.getNamePath() + "\n");
                }
                return false;
            }
            return true;
        }

        public TreeIterator traverseTree()
        {
            return new TreeIterator(getSelfNonConst());
        }

        public GraphIterator traverseGraph()
        {
            return new GraphIterator(getSelfNonConst());
        }

        public virtual Edge getUpstreamEdge(int index = 0)
        {
            return Mx.getNullEdge();
        }

        public virtual int getUpstreamEdgeCount()
        {
            return 0;
        }

        public Element getUpstreamElement(int index = 0)
        {
            return getUpstreamEdge(index).getUpstreamElement();
        }

        public InheritanceIterator traverseInheritance()
        {
            return new InheritanceIterator(getSelf());
        }

        public void setSourceUri(string sourceUri)
        {
            _sourceUri = sourceUri;
        }

        public bool hasSourceUri()
        {
            return _sourceUri.Length != 0;
        }

        public string getSourceUri()
        {
            return _sourceUri;
        }

        public string getActiveSourceUri()
        {
            for (Element elem = getSelf(); elem != null; elem = elem.getParent())
            {
                if (elem.hasSourceUri())
                {
                    return elem.getSourceUri();
                }
            }
            return Mx.EMPTY_STRING;
        }

        public bool belongsToContentDocument()
        {
            return getActiveSourceUri() == getDocument().getSourceUri();
        }

        public virtual bool validate(StringBuilder message = null)
        {
            bool res = true;
            validateRequire(Mx.isValidName(getName()), ref res, message, "Invalid element name");
            if (hasInheritString())
            {
                bool validInherit = getInheritsFrom() != null && getInheritsFrom().getCategory() == getCategory();
                validateRequire(validInherit, ref res, message, "Invalid element inheritance");
            }
            foreach (Element child in getChildren())
            {
                res = child.validate(message) && res;
            }
            validateRequire(!hasInheritanceCycle(), ref res, message, "Cycle in element inheritance chain");
            return res;
        }

        public void copyContentFrom(Element source)
        {
            getDocument().invalidateCache();

            _sourceUri = source._sourceUri;
            _attributeMap = new StringMap(source._attributeMap);
            _attributeOrder = new List<string>(source._attributeOrder);

            foreach (Element child in new List<Element>(source.getChildren()))
            {
                string name = child.getName();

                // Check for duplicate elements.
                Element previous = getChild(name);
                if (previous != null)
                {
                    continue;
                }

                // Create the copied element.
                Element childCopy = addChildOfCategory(child.getCategory(), name);
                childCopy.copyContentFrom(child);
            }
        }

        public virtual void clearContent()
        {
            getDocument().invalidateCache();

            _sourceUri = "";
            _attributeMap.Clear();
            _attributeOrder.Clear();
            _childMap.Clear();
            _childOrder.Clear();
        }

        public string createValidChildName(string name)
        {
            name = name.Length == 0 ? "_" : Mx.createValidName(name);
            while (getChild(name) != null)
            {
                name = Mx.incrementName(name);
            }
            return name;
        }

        public StringResolver createStringResolver(string geom = Mx.EMPTY_STRING)
        {
            StringResolver resolver = StringResolver.create();

            // Compute file and geom prefixes as this scope.
            resolver.setFilePrefix(getActiveFilePrefix());
            resolver.setGeomPrefix(getActiveGeomPrefix());

            // If a geometry name is specified, then use the corresponding UDIM.
            if (geom.Length != 0)
            {
                foreach (GeomInfo geomInfo in getDocument().getGeomInfos())
                {
                    if (!Mx.geomStringsMatch(geom, geomInfo.getActiveGeom()))
                    {
                        continue;
                    }
                    foreach (Token token in geomInfo.getTokens())
                    {
                        string key = "<" + token.getName() + ">";
                        string value = token.getResolvedValueString();
                        resolver.setFilenameSubstitution(key, value);
                    }
                }
            }

            // Add in token substitutions
            resolver.addTokenSubstitutions(getSelf());

            return resolver;
        }

        public string asString()
        {
            StringBuilder res = new StringBuilder("<" + getCategory());
            if (getName() != Mx.EMPTY_STRING)
            {
                res.Append(" name=\"" + getName() + "\"");
            }
            foreach (string attrName in getAttributeNames())
            {
                res.Append(" " + attrName + "=\"" + getAttribute(attrName) + "\"");
            }
            res.Append(">");
            return res.ToString();
        }

        public override string ToString()
        {
            return asString();
        }

        // Element Protected Methods
        protected T resolveNameReference<T>(string name, Element parent = null) where T : Element
        {
            Element scope = parent != null ? parent : getRoot();
            T child = scope.getChildOfType<T>(getQualifiedName(name));
            return child != null ? child : scope.getChildOfType<T>(name);
        }

        protected void validateRequire(bool expression, ref bool res, StringBuilder message, string errorDesc)
        {
            if (!expression)
            {
                res = false;
                if (message != null)
                {
                    message.Append(errorDesc + ": " + asString() + "\n");
                }
            }
        }

        protected virtual void registerChildElement(Element child)
        {
            getDocument().invalidateCache();
            _childMap[child.getName()] = child;
            _childOrder.Add(child);
        }

        protected virtual void unregisterChildElement(Element child)
        {
            getDocument().invalidateCache();
            _childMap.Remove(child.getName());
            int index = indexOfChild(child);
            if (index != -1)
            {
                _childOrder.RemoveAt(index);
            }
        }

        protected Element getSelfNonConst()
        {
            return this;
        }

        // Element Private Methods
        private int indexOfChild(Element child)
        {
            for (int i = 0; i < _childOrder.Count; i++)
            {
                if (ReferenceEquals(_childOrder[i], child))
                {
                    return i;
                }
            }
            return -1;
        }

        private static string categoryOf<T>() where T : Element
        {
            string category;
            if (!_categoryMap.TryGetValue(typeof(T), out category))
            {
                throw new Exception("Element type has no category: " + typeof(T).Name);
            }
            return category;
        }

        private static void registerCategory<T>(string category, Func<Element, string, Element> creator) where T : Element
        {
            _creatorMap[category] = creator;
            _categoryMap[typeof(T)] = category;
        }

        static Element()
        {
            registerCategory<AttributeDef>(AttributeDef.CATEGORY, (Element parent, string name) => new AttributeDef(parent, name));
            registerCategory<Backdrop>(Backdrop.CATEGORY, (Element parent, string name) => new Backdrop(parent, name));
            registerCategory<Collection>(Collection.CATEGORY, (Element parent, string name) => new Collection(parent, name));
            registerCategory<CommentElement>(CommentElement.CATEGORY, (Element parent, string name) => new CommentElement(parent, name));
            registerCategory<Document>(Document.CATEGORY, (Element parent, string name) => new Document(parent, name));
            registerCategory<GenericElement>(GenericElement.CATEGORY, (Element parent, string name) => new GenericElement(parent, name));
            registerCategory<GeomInfo>(GeomInfo.CATEGORY, (Element parent, string name) => new GeomInfo(parent, name));
            registerCategory<GeomProp>(GeomProp.CATEGORY, (Element parent, string name) => new GeomProp(parent, name));
            registerCategory<GeomPropDef>(GeomPropDef.CATEGORY, (Element parent, string name) => new GeomPropDef(parent, name));
            registerCategory<Implementation>(Implementation.CATEGORY, (Element parent, string name) => new Implementation(parent, name));
            registerCategory<Input>(Input.CATEGORY, (Element parent, string name) => new Input(parent, name));
            registerCategory<Look>(Look.CATEGORY, (Element parent, string name) => new Look(parent, name));
            registerCategory<LookGroup>(LookGroup.CATEGORY, (Element parent, string name) => new LookGroup(parent, name));
            registerCategory<MaterialAssign>(MaterialAssign.CATEGORY, (Element parent, string name) => new MaterialAssign(parent, name));
            registerCategory<Member>(Member.CATEGORY, (Element parent, string name) => new Member(parent, name));
            registerCategory<NewlineElement>(NewlineElement.CATEGORY, (Element parent, string name) => new NewlineElement(parent, name));
            registerCategory<Node>(Node.CATEGORY, (Element parent, string name) => new Node(parent, name));
            registerCategory<NodeDef>(NodeDef.CATEGORY, (Element parent, string name) => new NodeDef(parent, name));
            registerCategory<NodeGraph>(NodeGraph.CATEGORY, (Element parent, string name) => new NodeGraph(parent, name));
            registerCategory<Output>(Output.CATEGORY, (Element parent, string name) => new Output(parent, name));
            registerCategory<Property>(Property.CATEGORY, (Element parent, string name) => new Property(parent, name));
            registerCategory<PropertyAssign>(PropertyAssign.CATEGORY, (Element parent, string name) => new PropertyAssign(parent, name));
            registerCategory<PropertySet>(PropertySet.CATEGORY, (Element parent, string name) => new PropertySet(parent, name));
            registerCategory<PropertySetAssign>(PropertySetAssign.CATEGORY, (Element parent, string name) => new PropertySetAssign(parent, name));
            registerCategory<TargetDef>(TargetDef.CATEGORY, (Element parent, string name) => new TargetDef(parent, name));
            registerCategory<Token>(Token.CATEGORY, (Element parent, string name) => new Token(parent, name));
            registerCategory<TypeDef>(TypeDef.CATEGORY, (Element parent, string name) => new TypeDef(parent, name));
            registerCategory<Unit>(Unit.CATEGORY, (Element parent, string name) => new Unit(parent, name));
            registerCategory<UnitDef>(UnitDef.CATEGORY, (Element parent, string name) => new UnitDef(parent, name));
            registerCategory<UnitTypeDef>(UnitTypeDef.CATEGORY, (Element parent, string name) => new UnitTypeDef(parent, name));
            registerCategory<Variant>(Variant.CATEGORY, (Element parent, string name) => new Variant(parent, name));
            registerCategory<VariantAssign>(VariantAssign.CATEGORY, (Element parent, string name) => new VariantAssign(parent, name));
            registerCategory<VariantSet>(VariantSet.CATEGORY, (Element parent, string name) => new VariantSet(parent, name));
            registerCategory<Visibility>(Visibility.CATEGORY, (Element parent, string name) => new Visibility(parent, name));
        }

        // Element Public Members
        public const string NAME_ATTRIBUTE = "name";
        public const string FILE_PREFIX_ATTRIBUTE = "fileprefix";
        public const string GEOM_PREFIX_ATTRIBUTE = "geomprefix";
        public const string COLOR_SPACE_ATTRIBUTE = "colorspace";
        public const string INHERIT_ATTRIBUTE = "inherit";
        public const string NAMESPACE_ATTRIBUTE = "namespace";
        public const string DOC_ATTRIBUTE = "doc";
        public const string XPOS_ATTRIBUTE = "xpos";
        public const string YPOS_ATTRIBUTE = "ypos";

        // Element Protected Members
        protected string _category;
        protected string _name;
        protected string _sourceUri = "";

        protected Dictionary<string, Element> _childMap = new Dictionary<string, Element>(StringComparer.Ordinal);
        protected List<Element> _childOrder = new List<Element>();

        protected StringMap _attributeMap = new StringMap();
        protected List<string> _attributeOrder = new List<string>();

        protected Element _parent;
        protected Element _root;

        // Element Private Members
        private static readonly Dictionary<string, Func<Element, string, Element>> _creatorMap =
            new Dictionary<string, Func<Element, string, Element>>(StringComparer.Ordinal);
        private static readonly Dictionary<System.Type, string> _categoryMap = new Dictionary<System.Type, string>();
    }

    // TypedElement Definition
    public abstract class TypedElement : Element
    {
        // TypedElement Protected Methods
        protected TypedElement(Element parent, string category, string name) : base(parent, category, name)
        {
        }

        // TypedElement Public Methods
        public void setType(string type)
        {
            setAttribute(TYPE_ATTRIBUTE, type);
        }

        public bool hasType()
        {
            return hasAttribute(TYPE_ATTRIBUTE);
        }

        public virtual string getType()
        {
            return getAttribute(TYPE_ATTRIBUTE);
        }

        public bool isColorType()
        {
            return getType() == "color3" || getType() == "color4";
        }

        public bool isMultiOutputType()
        {
            return getType() == Mx.MULTI_OUTPUT_TYPE_STRING;
        }

        public TypeDef getTypeDef()
        {
            return resolveNameReference<TypeDef>(getType());
        }

        // TypedElement Public Members
        public const string TYPE_ATTRIBUTE = "type";
    }

    // ValueElement Definition
    public abstract class ValueElement : TypedElement
    {
        // ValueElement Protected Methods
        protected ValueElement(Element parent, string category, string name) : base(parent, category, name)
        {
        }

        // ValueElement Public Methods
        public void setValueString(string value)
        {
            setAttribute(VALUE_ATTRIBUTE, value);
        }

        public bool hasValueString()
        {
            return hasAttribute(VALUE_ATTRIBUTE);
        }

        public string getValueString()
        {
            return getAttribute(VALUE_ATTRIBUTE);
        }

        public string getResolvedValueString(StringResolver resolver = null)
        {
            if (!StringResolver.isResolvedType(getType()))
            {
                return getValueString();
            }
            if (resolver == null)
            {
                resolver = createStringResolver();
            }
            return resolver.resolve(getValueString(), getType());
        }

        public void setInterfaceName(string name)
        {
            setAttribute(INTERFACE_NAME_ATTRIBUTE, name);
        }

        public bool hasInterfaceName()
        {
            return hasAttribute(INTERFACE_NAME_ATTRIBUTE);
        }

        public string getInterfaceName()
        {
            return getAttribute(INTERFACE_NAME_ATTRIBUTE);
        }

        public void setImplementationName(string name)
        {
            setAttribute(IMPLEMENTATION_NAME_ATTRIBUTE, name);
        }

        public bool hasImplementationName()
        {
            return hasAttribute(IMPLEMENTATION_NAME_ATTRIBUTE);
        }

        public string getImplementationName()
        {
            return getAttribute(IMPLEMENTATION_NAME_ATTRIBUTE);
        }

        public void setValue<T>(T value, string type = Mx.EMPTY_STRING)
        {
            setType(type.Length != 0 ? type : Mx.getTypeString<T>());
            setValueString(Mx.toValueString<T>(value));
        }

        public bool hasValue()
        {
            return hasAttribute(VALUE_ATTRIBUTE);
        }

        public Value getValue()
        {
            if (!hasValue())
            {
                return null;
            }
            return Value.createValueFromStrings(getValueString(), getType(), getDocument().getTypeDef(getType()));
        }

        public Value getResolvedValue(StringResolver resolver = null)
        {
            if (!hasValue())
            {
                return null;
            }
            return Value.createValueFromStrings(getResolvedValueString(resolver), getType(), getDocument().getTypeDef(getType()));
        }

        public Value getDefaultValue()
        {
            Element parent = getParent();
            InterfaceElement interfaceElement = parent != null ? parent.asA<InterfaceElement>() : null;
            if (interfaceElement != null)
            {
                InterfaceElement decl = interfaceElement.getDeclaration();
                if (decl != null)
                {
                    ValueElement value = decl.getActiveValueElement(getName());
                    if (value != null)
                    {
                        return value.getValue();
                    }
                }
            }
            return null;
        }

        public void setUnit(string unit)
        {
            setAttribute(UNIT_ATTRIBUTE, unit);
        }

        public bool hasUnit()
        {
            return hasAttribute(UNIT_ATTRIBUTE);
        }

        public string getUnit()
        {
            return getAttribute(UNIT_ATTRIBUTE);
        }

        public string getActiveUnit()
        {
            // Search for a unit string in the associated nodedef.
            Element parent = getParent();
            InterfaceElement interfaceElement = parent != null ? parent.asA<InterfaceElement>() : null;
            if (interfaceElement != null)
            {
                InterfaceElement decl = interfaceElement.getDeclaration();
                if (decl != null)
                {
                    ValueElement value = decl.getActiveValueElement(getName());
                    if (value != null)
                    {
                        return value.getUnit();
                    }
                }
            }
            return Mx.EMPTY_STRING;
        }

        public void setUnitType(string unit)
        {
            setAttribute(UNITTYPE_ATTRIBUTE, unit);
        }

        public bool hasUnitType()
        {
            return hasAttribute(UNITTYPE_ATTRIBUTE);
        }

        public string getUnitType()
        {
            return getAttribute(UNITTYPE_ATTRIBUTE);
        }

        public void setIsUniform(bool value)
        {
            setTypedAttribute<bool>(UNIFORM_ATTRIBUTE, value);
        }

        public bool getIsUniform()
        {
            return getTypedAttribute<bool>(UNIFORM_ATTRIBUTE);
        }

        public override bool isAttributeEquivalent(Element rhs, string attributeName,
                                                   ElementEquivalenceOptions options, StringBuilder message = null)
        {
            // Perform value comparisons
            bool performedValueComparison = false;
            if (options.performValueComparisons)
            {
                using (ScopedFloatFormatting fmt = new ScopedFloatFormatting(options.floatFormat, options.floatPrecision))
                {
                    ValueElement rhsValueElement = rhs.asA<ValueElement>();

                    // Check value equality
                    if (rhsValueElement != null && attributeName == ValueElement.VALUE_ATTRIBUTE)
                    {
                        Value thisValue = getValue();
                        Value rhsValue = rhsValueElement.getValue();
                        if (thisValue != null && rhsValue != null)
                        {
                            if (thisValue.getValueString() != rhsValue.getValueString())
                            {
                                if (message != null)
                                {
                                    message.Append("Attribute `" + attributeName + "` of " + getNamePath() + " differs from " + rhs.getNamePath() + "\n");
                                }
                                return false;
                            }
                        }
                        performedValueComparison = true;
                    }

                    // Check ui attribute value equality
                    else if (attributeName == ValueElement.UI_MIN_ATTRIBUTE || attributeName == ValueElement.UI_MAX_ATTRIBUTE ||
                             attributeName == ValueElement.UI_SOFT_MIN_ATTRIBUTE || attributeName == ValueElement.UI_SOFT_MAX_ATTRIBUTE ||
                             attributeName == ValueElement.UI_STEP_ATTRIBUTE)
                    {
                        string uiAttribute = getAttribute(attributeName);
                        string rhsUiAttribute = rhs.getAttribute(attributeName);
                        Value uiValue = rhsUiAttribute.Length != 0 ? Value.createValueFromStrings(uiAttribute, getType()) : null;
                        Value rhsUiValue = rhsUiAttribute.Length != 0 ? Value.createValueFromStrings(rhsUiAttribute, getType()) : null;
                        if (uiValue != null && rhsUiValue != null)
                        {
                            if (uiValue.getValueString() != rhsUiValue.getValueString())
                            {
                                if (message != null)
                                {
                                    message.Append("Attribute `" + attributeName + "` of " + getNamePath() + " differs from " + rhs.getNamePath() + "\n");
                                }
                                return false;
                            }
                        }
                        performedValueComparison = true;
                    }
                }
            }

            // If did not peform a value comparison, perform the default comparison
            if (!performedValueComparison)
            {
                return base.isAttributeEquivalent(rhs, attributeName, options, message);
            }

            return true;
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            if (hasType() && hasValueString())
            {
                validateRequire(getValue() != null, ref res, message, "Invalid value");
            }
            if (hasInterfaceName())
            {
                validateRequire(isA<Input>() || isA<Token>(), ref res, message, "Only input and token elements support interface names");
                GraphElement graph = getAncestorOfType<GraphElement>();

                // Skip over any nodegraph elements which are the direct parent
                if (graph != null && graph == getParent())
                {
                    Element graphParent = graph.getParent();
                    graph = graphParent != null ? graphParent.getAncestorOfType<GraphElement>() : null;
                }

                if (graph != null)
                {
                    InterfaceElement decl = graph.getDeclaration();
                    if (decl == null)
                    {
                        decl = graph;
                    }
                    ValueElement valueElem = decl.getActiveValueElement(getInterfaceName());
                    validateRequire(valueElem != null, ref res, message, "Interface name not found in referenced declaration");
                    if (valueElem != null)
                    {
                        validateRequire(getType() == valueElem.getType(), ref res, message, "Interface name refers to value element of a different type");
                    }
                }
            }
            UnitTypeDef unitTypeDef = null;
            if (hasUnitType())
            {
                string unittype = getUnitType();
                if (unittype.Length != 0)
                {
                    unitTypeDef = getDocument().getUnitTypeDef(unittype);
                    validateRequire(unitTypeDef != null, ref res, message, "Unit type definition does not exist in document");
                }
            }
            if (hasUnit())
            {
                bool foundUnit = false;
                if (unitTypeDef != null)
                {
                    string unit = getUnit();
                    foreach (UnitDef unitDef in unitTypeDef.getUnitDefs())
                    {
                        if (unitDef.getUnit(unit) != null)
                        {
                            foundUnit = true;
                            break;
                        }
                    }
                }
                validateRequire(foundUnit, ref res, message, "Unit definition does not exist in document");
            }
            return base.validate(message) && res;
        }

        // ValueElement Public Members
        public const string VALUE_ATTRIBUTE = "value";
        public const string INTERFACE_NAME_ATTRIBUTE = "interfacename";
        public const string IMPLEMENTATION_NAME_ATTRIBUTE = "implname";
        public const string IMPLEMENTATION_TYPE_ATTRIBUTE = "impltype";
        public const string ENUM_ATTRIBUTE = "enum";
        public const string ENUM_VALUES_ATTRIBUTE = "enumvalues";
        public const string UI_NAME_ATTRIBUTE = "uiname";
        public const string UI_FOLDER_ATTRIBUTE = "uifolder";
        public const string UI_MIN_ATTRIBUTE = "uimin";
        public const string UI_MAX_ATTRIBUTE = "uimax";
        public const string UI_SOFT_MIN_ATTRIBUTE = "uisoftmin";
        public const string UI_SOFT_MAX_ATTRIBUTE = "uisoftmax";
        public const string UI_STEP_ATTRIBUTE = "uistep";
        public const string UI_ADVANCED_ATTRIBUTE = "uiadvanced";
        public const string UNIT_ATTRIBUTE = "unit";
        public const string UNITTYPE_ATTRIBUTE = "unittype";
        public const string UNIFORM_ATTRIBUTE = "uniform";
    }

    // Token Definition
    public class Token : ValueElement
    {
        public Token(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "token";
    }

    // CommentElement Definition
    public class CommentElement : Element
    {
        public CommentElement(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "comment";
    }

    // NewlineElement Definition
    public class NewlineElement : Element
    {
        public NewlineElement(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "newline";
    }

    // GenericElement Definition
    public class GenericElement : Element
    {
        public GenericElement(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "generic";
    }

    // StringResolver Definition
    public class StringResolver
    {
        // StringResolver Public Methods
        public static StringResolver create()
        {
            return new StringResolver();
        }

        public void setFilePrefix(string filePrefix)
        {
            _filePrefix = filePrefix;
        }

        public string getFilePrefix()
        {
            return _filePrefix;
        }

        public void setGeomPrefix(string geomPrefix)
        {
            _geomPrefix = geomPrefix;
        }

        public string getGeomPrefix()
        {
            return _geomPrefix;
        }

        public void setUdimString(string udim)
        {
            setFilenameSubstitution(Mx.UDIM_TOKEN, udim);
        }

        public void setUvTileString(string uvTile)
        {
            setFilenameSubstitution(Mx.UV_TILE_TOKEN, uvTile);
        }

        public void setFilenameSubstitution(string key, string value)
        {
            _filenameMap[key] = value;
        }

        public void addTokenSubstitutions(Element element)
        {
            const string DELIMITER_PREFIX = "[";
            const string DELIMITER_POSTFIX = "]";

            // Travese from sibliings up until root is reached.
            // Child tokens override any parent tokens.
            Element parent = element.getParent();
            while (parent != null)
            {
                InterfaceElement interfaceElem = parent.asA<InterfaceElement>();
                if (interfaceElem != null)
                {
                    List<Token> tokens = interfaceElem.getActiveTokens();
                    foreach (Token token in tokens)
                    {
                        string key = DELIMITER_PREFIX + token.getName() + DELIMITER_POSTFIX;
                        string value = token.getResolvedValueString();
                        if (!_filenameMap.ContainsKey(key))
                        {
                            setFilenameSubstitution(key, value);
                        }
                    }

                    // If we are in a nodegraph, also check its nodedef for tokens
                    NodeGraph nodegraph = parent.asA<NodeGraph>();
                    if (nodegraph != null)
                    {
                        NodeDef nodedef = nodegraph.getNodeDef();
                        if (nodedef != null)
                        {
                            tokens = nodedef.getActiveTokens();
                            foreach (Token token in tokens)
                            {
                                string key = DELIMITER_PREFIX + token.getName() + DELIMITER_POSTFIX;
                                string value = token.getResolvedValueString();
                                if (!_filenameMap.ContainsKey(key))
                                {
                                    setFilenameSubstitution(key, value);
                                }
                            }
                        }
                    }
                }
                parent = parent.getParent();
            }
        }

        public StringMap getFilenameSubstitutions()
        {
            return _filenameMap;
        }

        public void setGeomNameSubstitution(string key, string value)
        {
            _geomNameMap[key] = value;
        }

        public StringMap getGeomNameSubstitutions()
        {
            return _geomNameMap;
        }

        public virtual string resolve(string str, string type)
        {
            if (type == Mx.FILENAME_TYPE_STRING)
            {
                return _filePrefix + Mx.replaceSubstrings(str, _filenameMap);
            }
            if (type == Mx.GEOMNAME_TYPE_STRING)
            {
                return _geomPrefix + Mx.replaceSubstrings(str, _geomNameMap);
            }
            return str;
        }

        public static bool isResolvedType(string type)
        {
            return type == Mx.FILENAME_TYPE_STRING || type == Mx.GEOMNAME_TYPE_STRING;
        }

        // StringResolver Protected Methods
        protected StringResolver()
        {
        }

        // StringResolver Protected Members
        protected string _filePrefix = "";
        protected string _geomPrefix = "";
        protected StringMap _filenameMap = new StringMap();
        protected StringMap _geomNameMap = new StringMap();
    }

    // ElementEquivalenceOptions Definition
    public class ElementEquivalenceOptions
    {
        public ElementEquivalenceOptions()
        {
            performValueComparisons = true;
            floatFormat = Value.getFloatFormat();
            floatPrecision = Value.getFloatPrecision();
            attributeExclusionList = new StringSet();
        }

        public bool performValueComparisons;
        public Value.FloatFormat floatFormat;
        public int floatPrecision;
        public StringSet attributeExclusionList;
    }

    // ExceptionOrphanedElement Definition
    public class ExceptionOrphanedElement : Exception
    {
        public ExceptionOrphanedElement(string msg) : base(msg)
        {
        }
    }

    // Element Functions
    public static partial class Mx
    {
        // The value-initialised result of getTypedAttribute when the attribute is
        // absent or malformed: empty strings and arrays, zero otherwise.
        public static T defaultValue<T>()
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)EMPTY_STRING;
            }
            if (typeof(T) == typeof(List<string>))
            {
                return (T)(object)new List<string>();
            }
            if (typeof(T) == typeof(List<int>))
            {
                return (T)(object)new List<int>();
            }
            if (typeof(T) == typeof(List<bool>))
            {
                return (T)(object)new List<bool>();
            }
            if (typeof(T) == typeof(List<float>))
            {
                return (T)(object)new List<float>();
            }
            return default(T);
        }

        public static bool targetStringsMatch(string target1, string target2)
        {
            if (target1.Length == 0 || target2.Length == 0)
            {
                return true;
            }

            List<string> vec1 = splitString(target1, ARRAY_VALID_SEPARATORS);
            List<string> vec2 = splitString(target2, ARRAY_VALID_SEPARATORS);
            StringSet set1 = new StringSet(vec1);
            StringSet set2 = new StringSet(vec2);

            foreach (string item in set1)
            {
                if (set2.Contains(item))
                {
                    return true;
                }
            }
            return false;
        }

        public static string prettyPrint(Element elem)
        {
            StringBuilder text = new StringBuilder();
            for (TreeIterator it = elem.traverseTree().begin(); !it.isEnd(); it.next())
            {
                string indent = new string(' ', it.getElementDepth() * 2);
                text.Append(indent + it.getElement().asString() + "\n");
            }
            return text.ToString();
        }
    }
}
