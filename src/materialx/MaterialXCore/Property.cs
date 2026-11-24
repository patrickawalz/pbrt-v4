// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Property.h and Property.cpp.

using System.Collections.Generic;

namespace MaterialX
{
    // Property Definition
    public class Property : ValueElement
    {
        public Property(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public const string CATEGORY = "property";
    }

    // PropertyAssign Definition
    public class PropertyAssign : ValueElement
    {
        // PropertyAssign Public Methods
        public PropertyAssign(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setProperty(string value)
        {
            setAttribute(PROPERTY_ATTRIBUTE, value);
        }

        public bool hasProperty()
        {
            return hasAttribute(PROPERTY_ATTRIBUTE);
        }

        public string getProperty()
        {
            return getAttribute(PROPERTY_ATTRIBUTE);
        }

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

        // PropertyAssign Public Members
        public const string CATEGORY = "propertyassign";
        public const string PROPERTY_ATTRIBUTE = "property";
        public const string GEOM_ATTRIBUTE = "geom";
        public const string COLLECTION_ATTRIBUTE = "collection";
    }

    // PropertySet Definition
    public class PropertySet : Element
    {
        // PropertySet Public Methods
        public PropertySet(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public Property addProperty(string name)
        {
            return addChild<Property>(name);
        }

        public Property getProperty(string name)
        {
            return getChildOfType<Property>(name);
        }

        public List<Property> getProperties()
        {
            return getChildrenOfType<Property>();
        }

        public void removeProperty(string name)
        {
            removeChildOfType<Property>(name);
        }

        public Property setPropertyValue<T>(string name, T value, string type = Mx.EMPTY_STRING)
        {
            Property property = getChildOfType<Property>(name);
            if (property == null)
            {
                property = addProperty(name);
            }
            property.setValue(value, type);
            return property;
        }

        public Value getPropertyValue(string name)
        {
            Property property = getProperty(name);
            return property != null ? property.getValue() : null;
        }

        // PropertySet Public Members
        public const string CATEGORY = "propertyset";
    }

    // PropertySetAssign Definition
    public class PropertySetAssign : GeomElement
    {
        // PropertySetAssign Public Methods
        public PropertySetAssign(Element parent, string name) : base(parent, CATEGORY, name)
        {
        }

        public void setPropertySetString(string value)
        {
            setAttribute(PROPERTY_SET_ATTRIBUTE, value);
        }

        public bool hasPropertySetString()
        {
            return hasAttribute(PROPERTY_SET_ATTRIBUTE);
        }

        public string getPropertySetString()
        {
            return getAttribute(PROPERTY_SET_ATTRIBUTE);
        }

        public void setPropertySet(PropertySet propertySet)
        {
            if (propertySet != null)
            {
                setPropertySetString(propertySet.getName());
            }
            else
            {
                removeAttribute(PROPERTY_SET_ATTRIBUTE);
            }
        }

        public PropertySet getPropertySet()
        {
            return resolveNameReference<PropertySet>(getPropertySetString());
        }

        // PropertySetAssign Public Members
        public const string CATEGORY = "propertysetassign";
        public const string PROPERTY_SET_ATTRIBUTE = "propertyset";
    }
}
