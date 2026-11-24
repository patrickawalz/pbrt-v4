// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Unit.h and Unit.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaterialX
{
    // UnitConverter Definition
    public abstract class UnitConverter
    {
        // UnitConverter Public Methods
        public abstract float convert(float input, string inputUnit, string outputUnit);

        public virtual int getUnitAsInteger(string unitName)
        {
            return -1;
        }

        public virtual string getUnitFromInteger(int index)
        {
            return Mx.EMPTY_STRING;
        }

        public abstract Vector2 convert(Vector2 input, string inputUnit, string outputUnit);

        public abstract Vector3 convert(Vector3 input, string inputUnit, string outputUnit);

        public abstract Vector4 convert(Vector4 input, string inputUnit, string outputUnit);

        public abstract void write(Document doc);
    }

    // LinearUnitConverter Definition
    public class LinearUnitConverter : UnitConverter
    {
        // LinearUnitConverter Public Methods
        public static LinearUnitConverter create(UnitTypeDef unitTypeDef)
        {
            return new LinearUnitConverter(unitTypeDef);
        }

        public string getUnitType()
        {
            return _unitType;
        }

        public override void write(Document doc)
        {
            if (doc.getUnitTypeDef(_unitType) == null)
            {
                // Add a unittypedef
                doc.addUnitTypeDef(_unitType);

                // Add a unitdef
                string unitdefName = "UD_stdlib_" + _unitType;
                if (doc.getUnitDef(unitdefName) == null)
                {
                    UnitDef unitDef = doc.addUnitDef(unitdefName);
                    unitDef.setUnitType(_unitType);

                    // Add in units to the definition
                    foreach (KeyValuePair<string, float> unitScale in _unitScale)
                    {
                        string unitName = unitScale.Key;
                        Unit unitPtr = unitDef.addUnit(unitName);
                        unitPtr.setAttribute(SCALE_ATTRIBUTE, ((double)unitScale.Value).ToString("F6", CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        public Dictionary<string, float> getUnitScale()
        {
            return _unitScale;
        }

        public float conversionRatio(string inputUnit, string outputUnit)
        {
            float fromScale;
            if (!_unitScale.TryGetValue(inputUnit, out fromScale))
            {
                throw new ExceptionTypeError("Unrecognized source unit: " + inputUnit);
            }

            float toScale;
            if (!_unitScale.TryGetValue(outputUnit, out toScale))
            {
                throw new ExceptionTypeError("Unrecognized destination unit: " + outputUnit);
            }

            return fromScale / toScale;
        }

        public override float convert(float input, string inputUnit, string outputUnit)
        {
            if (inputUnit == outputUnit)
            {
                return input;
            }

            return input * conversionRatio(inputUnit, outputUnit);
        }

        public override Vector2 convert(Vector2 input, string inputUnit, string outputUnit)
        {
            if (inputUnit == outputUnit)
            {
                return input;
            }

            return input * conversionRatio(inputUnit, outputUnit);
        }

        public override Vector3 convert(Vector3 input, string inputUnit, string outputUnit)
        {
            if (inputUnit == outputUnit)
            {
                return input;
            }

            return input * conversionRatio(inputUnit, outputUnit);
        }

        public override Vector4 convert(Vector4 input, string inputUnit, string outputUnit)
        {
            if (inputUnit == outputUnit)
            {
                return input;
            }

            return input * conversionRatio(inputUnit, outputUnit);
        }

        public override int getUnitAsInteger(string unitName)
        {
            int found;
            if (_unitEnumeration.TryGetValue(unitName, out found))
            {
                return found;
            }
            return -1;
        }

        public override string getUnitFromInteger(int index)
        {
            foreach (KeyValuePair<string, int> e in _unitEnumeration)
            {
                if (e.Value == index)
                {
                    return e.Key;
                }
            }
            return Mx.EMPTY_STRING;
        }

        // LinearUnitConverter Private Methods
        private LinearUnitConverter(UnitTypeDef unitTypeDef)
        {
            if (unitTypeDef == null)
            {
                return;
            }

            // Populate the unit scale and offset maps for each UnitDef.
            int enumerant = 0;
            foreach (UnitDef unitdef in unitTypeDef.getUnitDefs())
            {
                foreach (Unit unit in unitdef.getUnits())
                {
                    string name = unit.getName();
                    if (name.Length != 0)
                    {
                        string scaleString = unit.getAttribute(SCALE_ATTRIBUTE);
                        if (scaleString.Length != 0)
                        {
                            Value scaleValue = Value.createValueFromStrings(scaleString, Mx.getTypeString<float>());
                            _unitScale[name] = scaleValue.asA<float>();
                        }
                        else
                        {
                            _unitScale[name] = 1.0f;
                        }
                        _unitEnumeration[name] = enumerant++;
                    }
                }
            }

            _unitType = unitTypeDef.getName();
        }

        // LinearUnitConverter Private Members
        private const string SCALE_ATTRIBUTE = "scale";

        private Dictionary<string, float> _unitScale = new Dictionary<string, float>(StringComparer.Ordinal);
        private Dictionary<string, int> _unitEnumeration = new Dictionary<string, int>(StringComparer.Ordinal);
        private string _unitType = "";
    }

    // UnitConverterRegistry Definition
    public class UnitConverterRegistry
    {
        // UnitConverterRegistry Public Methods
        public static UnitConverterRegistry create()
        {
            return registry;
        }

        public bool addUnitConverter(UnitTypeDef def, UnitConverter converter)
        {
            if (def != null && !_unitConverters.ContainsKey(def.getName()))
            {
                _unitConverters[def.getName()] = converter;
                return true;
            }
            return false;
        }

        public bool removeUnitConverter(UnitTypeDef def)
        {
            if (def != null)
            {
                if (_unitConverters.ContainsKey(def.getName()))
                {
                    _unitConverters.Remove(def.getName());
                    return true;
                }
            }
            return false;
        }

        public UnitConverter getUnitConverter(UnitTypeDef def)
        {
            if (def != null)
            {
                UnitConverter found;
                if (_unitConverters.TryGetValue(def.getName(), out found))
                {
                    return found;
                }
            }
            return null;
        }

        public void clearUnitConverters()
        {
            _unitConverters.Clear();
        }

        public int getUnitAsInteger(string unitName)
        {
            foreach (KeyValuePair<string, UnitConverter> it in _unitConverters)
            {
                int value = it.Value.getUnitAsInteger(unitName);
                if (value >= 0)
                {
                    return value;
                }
            }
            return -1;
        }

        public void write(Document doc)
        {
            foreach (KeyValuePair<string, UnitConverter> it in _unitConverters)
            {
                it.Value.write(doc);
            }
        }

        public bool convertToUnit(Document doc, string unitType, string targetUnit)
        {
            UnitTypeDef unitTypeDef = doc.getUnitTypeDef(unitType);
            UnitConverter unitConverter = getUnitConverter(unitTypeDef);

            if (unitTypeDef == null || unitConverter == null)
            {
                return false;
            }

            bool convertedUnits = false;
            foreach (Element elem in doc.traverseTree())
            {
                Node pNode = elem.asA<Node>();
                if (pNode == null || pNode.getInputCount() == 0)
                {
                    continue;
                }
                foreach (Input input in pNode.getInputs())
                {
                    string type = input.getType();
                    Value value = input.getValue();
                    if (value != null && input.hasUnit() && (input.getUnitType() == unitType))
                    {
                        if (type == Mx.getTypeString<float>())
                        {
                            float originalval = value.asA<float>();
                            float convertedValue = unitConverter.convert(originalval, input.getUnit(), targetUnit);
                            input.setValue<float>(convertedValue);
                            input.removeAttribute(ValueElement.UNIT_ATTRIBUTE);
                            input.removeAttribute(ValueElement.UNITTYPE_ATTRIBUTE);
                            convertedUnits = true;
                        }
                        else if (type == Mx.getTypeString<Vector2>())
                        {
                            Vector2 originalval = value.asA<Vector2>();
                            Vector2 convertedValue = unitConverter.convert(originalval, input.getUnit(), targetUnit);
                            input.setValue<Vector2>(convertedValue);
                            input.removeAttribute(ValueElement.UNIT_ATTRIBUTE);
                            input.removeAttribute(ValueElement.UNITTYPE_ATTRIBUTE);
                            convertedUnits = true;
                        }
                        else if (type == Mx.getTypeString<Vector3>())
                        {
                            Vector3 originalval = value.asA<Vector3>();
                            Vector3 convertedValue = unitConverter.convert(originalval, input.getUnit(), targetUnit);
                            input.setValue<Vector3>(convertedValue);
                            input.removeAttribute(ValueElement.UNIT_ATTRIBUTE);
                            input.removeAttribute(ValueElement.UNITTYPE_ATTRIBUTE);
                            convertedUnits = true;
                        }
                        else if (type == Mx.getTypeString<Vector4>())
                        {
                            Vector4 originalval = value.asA<Vector4>();
                            Vector4 convertedValue = unitConverter.convert(originalval, input.getUnit(), targetUnit);
                            input.setValue<Vector4>(convertedValue);
                            input.removeAttribute(ValueElement.UNIT_ATTRIBUTE);
                            input.removeAttribute(ValueElement.UNITTYPE_ATTRIBUTE);
                            convertedUnits = true;
                        }
                    }
                }
            }
            return convertedUnits;
        }

        // UnitConverterRegistry Private Methods
        private UnitConverterRegistry()
        {
        }

        // UnitConverterRegistry Private Members
        private static readonly UnitConverterRegistry registry = new UnitConverterRegistry();

        private Dictionary<string, UnitConverter> _unitConverters = new Dictionary<string, UnitConverter>(StringComparer.Ordinal);
    }
}
