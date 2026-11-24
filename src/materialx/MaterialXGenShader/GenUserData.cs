// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/GenUserData.h.

namespace MaterialX
{
    // GenUserData Definition
    // Base class for custom user data needed during shader generation.
    public abstract class GenUserData
    {
        // GenUserData Public Methods

        // Return a shared pointer for this object.
        public GenUserData getSelf()
        {
            return this;
        }

        // Return this object cast to a templated type.
        public T asA<T>() where T : GenUserData
        {
            return this as T;
        }

        // GenUserData Protected Methods
        protected GenUserData()
        {
        }
    }
}
