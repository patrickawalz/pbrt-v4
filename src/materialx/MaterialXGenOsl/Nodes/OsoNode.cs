// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenOsl/OsoNode.h and OsoNode.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // OsoNode Definition
    public class OsoNode : ShaderNodeImpl
    {
        // OsoNode Public Methods
        public static ShaderNodeImpl create()
        {
            return new OsoNode();
        }

        public override void initialize(InterfaceElement element, GenContext context)
        {
            base.initialize(element, context);

            if (!element.isA<Implementation>())
            {
                throw new ExceptionShaderGenError("Element '" + element.getName() + "' is not an Implementation element");
            }
            Implementation impl = (Implementation)(element);

            // Implementation's function attr is the oso filename, file attr is its directory
            _osoName = impl.getFunction();
            _osoPath = impl.getFile();

            // Set hash using the oso name.
            _hash = stringHash(_osoName);
        }

        public string getOsoName()
        {
            return _osoName;
        }

        public string getOsoPath()
        {
            return _osoPath;
        }

        // OsoNode Private Members
        private string _osoName = "";

        private string _osoPath = "";
    }
}
