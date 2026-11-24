// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/Factory.h.

using System;
using System.Collections.Generic;

namespace MaterialX
{
    // Factory Definition
    // Factory class for creating instances of classes given their type name.
    public class Factory<T> where T : class
    {
        // Factory Public Methods

        // Register a new class given a unique type name
        // and a creator function for the class.
        public void registerClass(string typeName, Func<T> f)
        {
            _creatorMap[typeName] = f;
        }

        // Determine if a class has been registered for a type name
        public bool classRegistered(string typeName)
        {
            return _creatorMap.ContainsKey(typeName);
        }

        // Unregister a registered class
        public void unregisterClass(string typeName)
        {
            _creatorMap.Remove(typeName);
        }

        // Create a new instance of the class with given type name.
        // Returns nullptr if no class with given name is registered.
        public T create(string typeName)
        {
            Func<T> creator;
            return _creatorMap.TryGetValue(typeName, out creator) ? creator() : null;
        }

        // Factory Private Members
        private Dictionary<string, Func<T>> _creatorMap = new Dictionary<string, Func<T>>(StringComparer.Ordinal);
    }
}
