// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Geom.h and Geom.cpp.

using System;
using System.Collections.Generic;
using System.Text;

namespace MaterialX
{
    // Geometry Constants and Functions
    public static partial class Mx
    {
        public const string GEOM_PATH_SEPARATOR = "/";
        public const string UNIVERSAL_GEOM_NAME = GEOM_PATH_SEPARATOR;
        public const string UDIM_TOKEN = "<UDIM>";
        public const string UV_TILE_TOKEN = "<UVTILE>";
        public const string UDIM_SET_PROPERTY = "udimset";

        public static bool geomStringsMatch(string geom1, string geom2, bool contains = false)
        {
            List<GeomPath> paths1 = new List<GeomPath>();
            foreach (string name1 in splitString(geom1, ARRAY_VALID_SEPARATORS))
            {
                paths1.Add(new GeomPath(name1));
            }
            foreach (string name2 in splitString(geom2, ARRAY_VALID_SEPARATORS))
            {
                GeomPath path2 = new GeomPath(name2);
                foreach (GeomPath path1 in paths1)
                {
                    if (path1.isMatching(path2, contains))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    // GeomPath Definition
    public class GeomPath
    {
        // GeomPath Public Methods
        public GeomPath()
        {
            _vec = new List<string>();
            _empty = true;
        }

        public GeomPath(string geom)
        {
            _vec = Mx.splitString(geom, Mx.GEOM_PATH_SEPARATOR);
            _empty = geom.Length == 0;
        }

        public bool isEqual(GeomPath rhs)
        {
            return System.Linq.Enumerable.SequenceEqual(_vec, rhs._vec) &&
                   _empty == rhs._empty;
        }

        public string asString()
        {
            if (_vec.Count == 0)
            {
                return _empty ? Mx.EMPTY_STRING : Mx.UNIVERSAL_GEOM_NAME;
            }
            return Mx.GEOM_PATH_SEPARATOR + Mx.joinStrings(_vec, Mx.GEOM_PATH_SEPARATOR);
        }

        public override string ToString()
        {
            return asString();
        }

        public bool isMatching(GeomPath rhs, bool contains = false)
        {
            if (_empty || rhs._empty)
            {
                return false;
            }
            if (contains && _vec.Count > rhs._vec.Count)
            {
                return false;
            }
            int minSize = Math.Min(_vec.Count, rhs._vec.Count);
            for (int i = 0; i < minSize; i++)
            {
                if (_vec[i] != rhs._vec[i])
                {
                    return false;
                }
            }
            return true;
        }

        public bool isEmpty()
        {
            return _empty;
        }

        public bool isUniversal()
        {
            return _vec.Count == 0 && !_empty;
        }

        // GeomPath Private Members
        private List<string> _vec;
        private bool _empty;
    }

    // GeomElement Definition
    public abstract class GeomElement : Element
    {
        // GeomElement Protected Methods
        protected GeomElement(Element parent, string category, string name) : base(parent, category, name)
        {
        }

        // GeomElement Public Methods
        public void setGeom(string value)
        {
            setAttribute(GEOM_ATTRIBUTE, value);
        }

        public bool hasGeom()
        {
            return hasAttribute(GEOM_ATTRIBUTE);
        }

        public string getGeom()
        {
            return getAttribute(GEOM_ATTRIBUTE);
        }

        public string getActiveGeom()
        {
            return hasGeom() ?
                   createStringResolver().resolve(getGeom(), Mx.GEOMNAME_TYPE_STRING) :
                   Mx.EMPTY_STRING;
        }

        public void setCollectionString(string value)
        {
            setAttribute(COLLECTION_ATTRIBUTE, value);
        }

        public bool hasCollectionString()
        {
            return hasAttribute(COLLECTION_ATTRIBUTE);
        }

        public string getCollectionString()
        {
            return getAttribute(COLLECTION_ATTRIBUTE);
        }

        public void setCollection(Collection collection)
        {
            if (collection != null)
            {
                setCollectionString(collection.getName());
            }
            else
            {
                removeAttribute(COLLECTION_ATTRIBUTE);
            }
        }

        public Collection getCollection()
        {
            return resolveNameReference<Collection>(getCollectionString());
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            if (hasCollectionString())
            {
                validateRequire(getCollection() != null, ref res, message, "Invalid collection string");
            }
            return base.validate(message) && res;
        }

        // GeomElement Public Members
        public const string GEOM_ATTRIBUTE = "geom";
        public const string COLLECTION_ATTRIBUTE = "collection";
    }

    // GeomInfo Definition
    public class GeomInfo : GeomElement
    {
        // GeomInfo Public Methods
        public GeomInfo(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public GeomProp addGeomProp(string name = Mx.EMPTY_STRING)
        {
            return addChild<GeomProp>(name);
        }

        public GeomProp getGeomProp(string name)
        {
            return getChildOfType<GeomProp>(name);
        }

        public List<GeomProp> getGeomProps()
        {
            return getChildrenOfType<GeomProp>();
        }

        public void removeGeomProp(string name)
        {
            removeChildOfType<GeomProp>(name);
        }

        public Token addToken(string name = Mx.EMPTY_STRING)
        {
            return addChild<Token>(name);
        }

        public Token getToken(string name)
        {
            return getChildOfType<Token>(name);
        }

        public List<Token> getTokens()
        {
            return getChildrenOfType<Token>();
        }

        public void removeToken(string name)
        {
            removeChildOfType<Token>(name);
        }

        public GeomProp setGeomPropValue<T>(string name, T value, string type = Mx.EMPTY_STRING)
        {
            GeomProp geomProp = getChildOfType<GeomProp>(name);
            if (geomProp == null)
            {
                geomProp = addGeomProp(name);
            }
            geomProp.setValue(value, type);
            return geomProp;
        }

        public Token setTokenValue(string name, string value)
        {
            Token token = getToken(name);
            if (token == null)
            {
                token = addToken(name);
            }
            token.setValue<string>(value);
            return token;
        }

        // GeomInfo Public Members
        public const string CATEGORY = "geominfo";
    }

    // GeomProp Definition
    public class GeomProp : ValueElement
    {
        public GeomProp(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "geomprop";
    }

    // GeomPropDef Definition
    public class GeomPropDef : TypedElement
    {
        // GeomPropDef Public Methods
        public GeomPropDef(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setGeomProp(string value)
        {
            setAttribute(GEOM_PROP_ATTRIBUTE, value);
        }

        public bool hasGeomProp()
        {
            return hasAttribute(GEOM_PROP_ATTRIBUTE);
        }

        public string getGeomProp()
        {
            return getAttribute(GEOM_PROP_ATTRIBUTE);
        }

        public void setSpace(string value)
        {
            setAttribute(SPACE_ATTRIBUTE, value);
        }

        public bool hasSpace()
        {
            return hasAttribute(SPACE_ATTRIBUTE);
        }

        public string getSpace()
        {
            return getAttribute(SPACE_ATTRIBUTE);
        }

        public void setIndex(string value)
        {
            setAttribute(INDEX_ATTRIBUTE, value);
        }

        public bool hasIndex()
        {
            return hasAttribute(INDEX_ATTRIBUTE);
        }

        public string getIndex()
        {
            return getAttribute(INDEX_ATTRIBUTE);
        }

        // GeomPropDef Public Members
        public const string CATEGORY = "geompropdef";
        public const string GEOM_PROP_ATTRIBUTE = "geomprop";
        public const string SPACE_ATTRIBUTE = "space";
        public const string INDEX_ATTRIBUTE = "index";
    }

    // Collection Definition
    public class Collection : Element
    {
        // Collection Public Methods
        public Collection(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setIncludeGeom(string value)
        {
            setAttribute(INCLUDE_GEOM_ATTRIBUTE, value);
        }

        public bool hasIncludeGeom()
        {
            return hasAttribute(INCLUDE_GEOM_ATTRIBUTE);
        }

        public string getIncludeGeom()
        {
            return getAttribute(INCLUDE_GEOM_ATTRIBUTE);
        }

        public string getActiveIncludeGeom()
        {
            return hasIncludeGeom() ?
                   createStringResolver().resolve(getIncludeGeom(), Mx.GEOMNAME_TYPE_STRING) :
                   Mx.EMPTY_STRING;
        }

        public void setExcludeGeom(string value)
        {
            setAttribute(EXCLUDE_GEOM_ATTRIBUTE, value);
        }

        public bool hasExcludeGeom()
        {
            return hasAttribute(EXCLUDE_GEOM_ATTRIBUTE);
        }

        public string getExcludeGeom()
        {
            return getAttribute(EXCLUDE_GEOM_ATTRIBUTE);
        }

        public string getActiveExcludeGeom()
        {
            return hasExcludeGeom() ?
                   createStringResolver().resolve(getExcludeGeom(), Mx.GEOMNAME_TYPE_STRING) :
                   Mx.EMPTY_STRING;
        }

        public void setIncludeCollectionString(string value)
        {
            setAttribute(INCLUDE_COLLECTION_ATTRIBUTE, value);
        }

        public bool hasIncludeCollectionString()
        {
            return hasAttribute(INCLUDE_COLLECTION_ATTRIBUTE);
        }

        public string getIncludeCollectionString()
        {
            return getAttribute(INCLUDE_COLLECTION_ATTRIBUTE);
        }

        public void setIncludeCollection(Collection collection)
        {
            if (collection != null)
            {
                setIncludeCollectionString(collection.getName());
            }
            else
            {
                removeAttribute(INCLUDE_COLLECTION_ATTRIBUTE);
            }
        }

        public void setIncludeCollections(List<Collection> collections)
        {
            if (collections.Count != 0)
            {
                List<string> stringVec = new List<string>();
                foreach (Collection collection in collections)
                {
                    stringVec.Add(collection.getName());
                }
                setTypedAttribute(INCLUDE_COLLECTION_ATTRIBUTE, stringVec);
            }
            else
            {
                removeAttribute(INCLUDE_COLLECTION_ATTRIBUTE);
            }
        }

        public List<Collection> getIncludeCollections()
        {
            List<Collection> vec = new List<Collection>();
            foreach (string str in getTypedAttribute<List<string>>(INCLUDE_COLLECTION_ATTRIBUTE))
            {
                Collection collection = resolveNameReference<Collection>(str);
                if (collection != null)
                {
                    vec.Add(collection);
                }
            }
            return vec;
        }

        public bool hasIncludeCycle()
        {
            try
            {
                matchesGeomString(Mx.UNIVERSAL_GEOM_NAME);
            }
            catch (ExceptionFoundCycle)
            {
                return true;
            }
            return false;
        }

        public bool matchesGeomString(string geom)
        {
            if (Mx.geomStringsMatch(getActiveExcludeGeom(), geom, true))
            {
                return false;
            }
            if (Mx.geomStringsMatch(getActiveIncludeGeom(), geom))
            {
                return true;
            }

            HashSet<Collection> includedSet = new HashSet<Collection>(ReferenceEqualityComparer.Instance);
            List<Collection> includedVec = getIncludeCollections();
            for (int i = 0; i < includedVec.Count; i++)
            {
                Collection collection = includedVec[i];
                if (includedSet.Contains(collection))
                {
                    throw new ExceptionFoundCycle("Encountered a cycle in collection: " + getName());
                }
                includedSet.Add(collection);
                List<Collection> appendVec = collection.getIncludeCollections();
                includedVec.AddRange(appendVec);
            }
            foreach (Collection collection in includedSet)
            {
                if (collection.matchesGeomString(geom))
                {
                    return true;
                }
            }

            return false;
        }

        public override bool validate(StringBuilder message = null)
        {
            bool res = true;
            validateRequire(!hasIncludeCycle(), ref res, message, "Cycle in collection include chain");
            return base.validate(message) && res;
        }

        // Collection Public Members
        public const string CATEGORY = "collection";
        public const string INCLUDE_GEOM_ATTRIBUTE = "includegeom";
        public const string EXCLUDE_GEOM_ATTRIBUTE = "excludegeom";
        public const string INCLUDE_COLLECTION_ATTRIBUTE = "includecollection";
    }
}
