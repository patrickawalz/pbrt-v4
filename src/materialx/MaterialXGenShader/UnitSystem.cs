// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/UnitSystem.h and UnitSystem.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // UnitTransform Definition
    // @struct @UnitTransform
    // Structure that represents unit transform information
    public class UnitTransform
    {
        // UnitTransform Public Methods
        public UnitTransform(string ss, string ts, TypeDesc t, string unittype)
        {
            sourceUnit = ss;
            targetUnit = ts;
            type = t;
            unitType = unittype;

            if (type != Type.FLOAT && type != Type.VECTOR2 && type != Type.VECTOR3 && type != Type.VECTOR4)
            {
                throw new ExceptionShaderGenError("Unit space transform can only be a float or vectors");
            }
        }

        // UnitTransform Public Members
        public string sourceUnit = "";

        public string targetUnit = "";

        public TypeDesc type;

        public string unitType = "";

        // Comparison operator (operator== in the C++)
        public bool isEqual(UnitTransform other)
        {
            return sourceUnit == other.sourceUnit &&
                   targetUnit == other.targetUnit &&
                   type == other.type &&
                   unitType == other.unitType;
        }
    }

    // UnitSystem Definition
    // Base unit system support
    public class UnitSystem
    {
        // UnitSystem Public Methods
        // Create a new UnitSystem
        public static UnitSystem create(string language)
        {
            return new UnitSystem(language);
        }

        // Return the UnitSystem name
        public virtual string getName()
        {
            return UnitSystem.UNITSYSTEM_NAME;
        }

        // Assign unit converter registry replacing any previous assignment
        public virtual void setUnitConverterRegistry(UnitConverterRegistry registry)
        {
            _unitRegistry = registry;
        }

        // Returns the currently assigned unit converter registry
        public virtual UnitConverterRegistry getUnitConverterRegistry()
        {
            return _unitRegistry;
        }

        // assign document with unit implementations replacing any previously loaded content.
        public virtual void loadLibrary(Document document)
        {
            _document = document;
        }

        // Returns whether this unit system supports a provided transform
        public bool supportsTransform(UnitTransform transform)
        {
            NodeDef nodeDef = getNodeDef(transform);
            return nodeDef != null;
        }

        // Create a node to use to perform the given unit space transformation.
        public ShaderNode createNode(ShaderGraph parent, UnitTransform transform, string name, GenContext context)
        {
            NodeDef nodeDef = getNodeDef(transform);
            if (nodeDef == null)
            {
                throw new ExceptionShaderGenError("No nodedef found for transform: ('" + transform.sourceUnit + "', '" + transform.targetUnit + "').");
            }

            // Scalar unit conversion
            UnitTypeDef scalarTypeDef = _document.getUnitTypeDef(transform.unitType);
            if (_unitRegistry == null || _unitRegistry.getUnitConverter(scalarTypeDef) == null)
            {
                throw new ExceptionTypeError("Unit registry unavailable or undefined unit converter for: " + transform.unitType);
            }
            LinearUnitConverter scalarConverter = _unitRegistry.getUnitConverter(scalarTypeDef) as LinearUnitConverter;

            // Create the node.
            ShaderNode shaderNode = ShaderNode.create(parent, name, nodeDef, context);

            // Set ports on the node.
            ShaderInput in2 = shaderNode.getInput("in2");
            if (in2 == null)
            {
                throw new ExceptionShaderGenError("Invalid node signature for unit transform: ('" + transform.sourceUnit + "', '" + transform.targetUnit + "').");
            }
            float conversionRatio = scalarConverter.conversionRatio(transform.sourceUnit, transform.targetUnit);
            in2.setValue(Value.createValue(conversionRatio));

            return shaderNode;
        }

        // Returns a nodedef for a given transform
        public virtual NodeDef getNodeDef(UnitTransform transform)
        {
            if (_document == null)
            {
                throw new ExceptionShaderGenError("No library loaded for unit system");
            }

            string MULTIPLY_NODE_NAME = "multiply";
            foreach (NodeDef nodeDef in _document.getMatchingNodeDefs(MULTIPLY_NODE_NAME))
            {
                List<Input> nodeInputs = nodeDef.getInputs();
                if (nodeInputs.Count == 2 &&
                    nodeInputs[0].getType() == transform.type.getName() &&
                    nodeInputs[1].getType() == "float")
                {
                    return nodeDef;
                }
            }
            return null;
        }

        // UnitSystem Public Members
        public const string UNITSYSTEM_NAME = "default_unit_system";

        // UnitSystem Protected Methods
        // Protected constructor
        protected UnitSystem(string target)
        {
            _target = createValidName(target);
        }

        // UnitSystem Protected Members
        protected UnitConverterRegistry _unitRegistry;

        protected Document _document;

        protected string _target = "";
    }

    // ScalarUnitNode Definition
    // Unit transform for scalar units, defined in the source file of the unit system.
    public class ScalarUnitNode : ShaderNodeImpl
    {
        // ScalarUnitNode Public Methods
        public ScalarUnitNode(LinearUnitConverter scalarUnitConverter)
        {
            _scalarUnitConverter = scalarUnitConverter;
            _unitRatioFunctionName = "mx_" + _scalarUnitConverter.getUnitType() + "_unit_ratio";
        }

        public static ShaderNodeImpl create(LinearUnitConverter scalarUnitConverter)
        {
            return new ScalarUnitNode(scalarUnitConverter);
        }

        public override void initialize(InterfaceElement element, GenContext context)
        {
            _name = element.getName();

            // Use the unit ratio function name has hash to make sure this function
            // is shared, and only emitted once, for all units of the same unit type.
            _hash = stringHash(_unitRatioFunctionName);
        }

        public override void emitFunctionDefinition(ShaderNode node, GenContext context, ShaderStage stage)
        {
            if (stage.getName() == Stage.PIXEL)
            {
                // Emit the helper function mx_<unittype>_unit_ratio that embeds a look up table for unit scale
                List<float> unitScales = new List<float>();
                Dictionary<string, float> unitScaleMap = _scalarUnitConverter.getUnitScale();
                for (int i = 0; i < unitScaleMap.Count; i++)
                {
                    unitScales.Add(0.0f);
                }
                foreach (KeyValuePair<string, float> unitScale in unitScaleMap)
                {
                    int location = _scalarUnitConverter.getUnitAsInteger(unitScale.Key);
                    unitScales[location] = unitScale.Value;
                }
                // See stdlib/gen*/mx_<unittype>_unit. This helper function is called by these shaders.
                string VAR_UNIT_SCALE = "u_" + _scalarUnitConverter.getUnitType() + "_unit_scales";
                VariableBlock unitLUT = new VariableBlock("unitLUT", EMPTY_STRING);
                using ScopedFloatFormatting fmt = new ScopedFloatFormatting(Value.FloatFormat.FloatFormatFixed, 15);
                unitLUT.add(Type.FLOATARRAY, VAR_UNIT_SCALE, Value.createValue<List<float>>(unitScales));

                ShaderGenerator shadergen = context.getShaderGenerator();
                shadergen.emitLine("float " + _unitRatioFunctionName + "(int unit_from, int unit_to)", stage, false);
                shadergen.emitFunctionBodyBegin(node, context, stage);
                shadergen.emitVariableDeclarations(unitLUT, shadergen.getSyntax().getConstantQualifier(), ";", context, stage, true);
                shadergen.emitLine("return (" + VAR_UNIT_SCALE + "[unit_from] / " + VAR_UNIT_SCALE + "[unit_to])", stage);
                shadergen.emitFunctionBodyEnd(node, context, stage);
            }
        }

        public override void emitFunctionCall(ShaderNode node, GenContext context, ShaderStage stage)
        {
            if (stage.getName() == Stage.PIXEL)
            {
                ShaderGenerator shadergen = context.getShaderGenerator();

                ShaderInput @in = node.getInput(0);
                ShaderInput from = node.getInput(1);
                ShaderInput to = node.getInput(2);

                shadergen.emitLineBegin(stage);
                shadergen.emitOutput(node.getOutput(), true, false, context, stage);
                shadergen.emitString(" = ", stage);
                shadergen.emitInput(@in, context, stage);
                shadergen.emitString(" * ", stage);
                shadergen.emitString(_unitRatioFunctionName + "(", stage);
                shadergen.emitInput(from, context, stage);
                shadergen.emitString(", ", stage);
                shadergen.emitInput(to, context, stage);
                shadergen.emitString(")", stage);
                shadergen.emitLineEnd(stage);
            }
        }

        // ScalarUnitNode Protected Members
        protected LinearUnitConverter _scalarUnitConverter;
        protected readonly string _unitRatioFunctionName;
    }
}
