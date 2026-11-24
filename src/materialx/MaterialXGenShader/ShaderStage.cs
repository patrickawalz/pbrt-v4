// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ShaderStage.h
// and ShaderStage.cpp.
//
// The macros DEFINE_SHADER_STAGE, BEGIN_SHADER_STAGE and END_SHADER_STAGE are
// written out as the test "stage.getName() == name" where the C++ uses them.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // Stage Definition
    // Identifiers for shader stages (the namespace Stage in the C++).
    public static class Stage
    {
        // Identifier for pixel stage.
        // This is the main stage used by all shader targets.
        // For single stage shader targets this is the one
        // and only stage.
        // Shader targets with multiple stages can add additional
        // stage identifiers to the Stage namespace.
        public const string PIXEL = "pixel";

        // Identifier for vertex stage.
        public const string VERTEX = "vertex";
    }

    // VariableBlock Definition
    // A block of variables in a shader stage
    public class VariableBlock
    {
        // VariableBlock Public Methods
        public VariableBlock(string name, string instance)
        {
            _name = name;
            _instance = instance;
        }

        // Get the name of this block.
        public string getName()
        {
            return _name;
        }

        // Set the name of this block.
        public void setName(string name)
        {
            _name = name;
        }

        // Get the instance name of this block.
        public string getInstance()
        {
            return _instance;
        }

        // Set the instance name of this block.
        public void setInstance(string instance)
        {
            _instance = instance;
        }

        // Return true if the block has no variables.
        public bool empty()
        {
            return _variableOrder.Count == 0;
        }

        // Return the number of variables in this block.
        public int size()
        {
            return _variableOrder.Count;
        }

        // Return a variable by index.
        public ShaderPort this[int index]
        {
            get
            {
                return _variableOrder[index];
            }
        }

        // Return a const reference to our variable order vector.
        public List<ShaderPort> getVariableOrder()
        {
            return _variableOrder;
        }

        // Return a variable by name. Throws exception if
        // no variable is found by the given name.
        public ShaderPort this[string name]
        {
            get
            {
                ShaderPort v = find(name);
                if (v == null)
                {
                    throw new ExceptionShaderGenError("No variable named '" + name + "' exists for block '" + getName() + "'");
                }
                return v;
            }
        }

        // Return a variable by name. Returns nullptr if
        // no variable is found by the given name.
        public ShaderPort find(string name)
        {
            ShaderPort found;
            return _variableMap.TryGetValue(name, out found) ? found : null;
        }

        // Find a port based on a predicate
        public ShaderPort find(Func<ShaderPort, bool> predicate)
        {
            foreach (ShaderPort port in getVariableOrder())
            {
                if (predicate(port))
                {
                    return port;
                }
            }
            return null;
        }

        // Add a new shader port to this block.
        public ShaderPort add(TypeDesc type, string name, Value value = null, bool shouldWiden = false)
        {
            ShaderPort existing;
            if (_variableMap.TryGetValue(name, out existing))
            {
                if (shouldWiden)
                {
                    // Automatically try to widen the type of the shader port if the requested type differs from
                    // the existing port's type.
                    if (existing.getType().getSize() < type.getSize())
                    {
                        existing.setType(type);
                    }
                }
                else if (type != existing.getType())
                {
                    throw new ExceptionShaderGenError("Trying to add shader port '" + name + "' with type '" +
                                                      type.getName() + "', but existing shader port with type '" +
                                                      existing.getType().getName() + "' was found");
                }
                return existing;
            }

            ShaderPort port = new ShaderPort(null, type, name, value);
            _variableMap[name] = port;
            _variableOrder.Add(port);

            return port;
        }

        // Add an existing shader port to this block.
        public void add(ShaderPort port)
        {
            if (!_variableMap.ContainsKey(port.getName()))
            {
                _variableMap[port.getName()] = port;
                _variableOrder.Add(port);
            }
        }

        // VariableBlock Private Members
        private string _name;
        private string _instance;
        private Dictionary<string, ShaderPort> _variableMap = new Dictionary<string, ShaderPort>(StringComparer.Ordinal);
        private List<ShaderPort> _variableOrder = new List<ShaderPort>();
    }

    // ShaderStage Definition
    // A shader stage, containing the state and
    // resulting source code for the stage.
    public class ShaderStage
    {
        // ShaderStage Public Types

        // Contains information on source code scope for the stage.
        public class Scope
        {
            public Syntax.Punctuation punctuation;
            public HashSet<ShaderNode> functions = new HashSet<ShaderNode>(ReferenceEqualityComparer.Instance);

            public Scope(Syntax.Punctuation p)
            {
                punctuation = p;
            }
        }

        // ShaderStage Public Methods

        // Contructor.
        public ShaderStage(string name, Syntax syntax)
        {
            _name = name;
            _syntax = syntax;
            _indentations = 0;
            _constants = new VariableBlock("Constants", "cn");
        }

        // Return the stage name.
        public string getName()
        {
            return _name;
        }

        // Return the stage function name.
        public string getFunctionName()
        {
            return _functionName;
        }

        // Set the stage source code.
        public void setSourceCode(string code)
        {
            _code.Clear();
            _code.Append(code);
        }

        // Return the stage source code.
        public string getSourceCode()
        {
            return _code.ToString();
        }

        // Create a new uniform variable block.
        public VariableBlock createUniformBlock(string name, string instance = EMPTY_STRING)
        {
            VariableBlock found;
            if (!_uniforms.TryGetValue(name, out found))
            {
                VariableBlock b = new VariableBlock(name, instance);
                _uniforms[name] = b;
                return b;
            }
            return found;
        }

        // Create a new input variable block.
        public VariableBlock createInputBlock(string name, string instance = EMPTY_STRING)
        {
            VariableBlock found;
            if (!_inputs.TryGetValue(name, out found))
            {
                VariableBlock b = new VariableBlock(name, instance);
                _inputs[name] = b;
                return b;
            }
            return found;
        }

        // Create a new output variable block.
        public VariableBlock createOutputBlock(string name, string instance = EMPTY_STRING)
        {
            VariableBlock found;
            if (!_outputs.TryGetValue(name, out found))
            {
                VariableBlock b = new VariableBlock(name, instance);
                _outputs[name] = b;
                return b;
            }
            return found;
        }

        // Return the uniform variable block with given name.
        public VariableBlock getUniformBlock(string name)
        {
            VariableBlock found;
            if (!_uniforms.TryGetValue(name, out found))
            {
                throw new ExceptionShaderGenError("No uniform block named '" + name + "' exists for shader stage '" + getName() + "'");
            }
            return found;
        }

        // Return the input variable block with given name.
        public VariableBlock getInputBlock(string name)
        {
            VariableBlock found;
            if (!_inputs.TryGetValue(name, out found))
            {
                throw new ExceptionShaderGenError("No input block named '" + name + "' exists for shader stage '" + getName() + "'");
            }
            return found;
        }

        // Return the output variable block with given name.
        public VariableBlock getOutputBlock(string name)
        {
            VariableBlock found;
            if (!_outputs.TryGetValue(name, out found))
            {
                throw new ExceptionShaderGenError("No output block named '" + name + "' exists for shader stage '" + getName() + "'");
            }
            return found;
        }

        // Return the constant variable block.
        public VariableBlock getConstantBlock()
        {
            return _constants;
        }

        // Return a map of all uniform blocks.
        public Dictionary<string, VariableBlock> getUniformBlocks()
        {
            return _uniforms;
        }

        // Return a map of all input blocks.
        public Dictionary<string, VariableBlock> getInputBlocks()
        {
            return _inputs;
        }

        // Return a map of all output blocks.
        public Dictionary<string, VariableBlock> getOutputBlocks()
        {
            return _outputs;
        }

        // Return a set of all include files.
        public StringSet getIncludes()
        {
            return _includes;
        }

        // Return a set of all source dependencies.
        public StringSet getSourceDependencies()
        {
            return _sourceDependencies;
        }

        // Start a new scope using the given bracket type.
        public void beginScope(Syntax.Punctuation punc = Syntax.Punctuation.CURLY_BRACKETS)
        {
            switch (punc)
            {
                case Syntax.Punctuation.CURLY_BRACKETS:
                    beginLine();
                    _code.Append("{" + _syntax.getNewline());
                    break;
                case Syntax.Punctuation.PARENTHESES:
                    beginLine();
                    _code.Append("(" + _syntax.getNewline());
                    break;
                case Syntax.Punctuation.SQUARE_BRACKETS:
                    beginLine();
                    _code.Append("[" + _syntax.getNewline());
                    break;
                case Syntax.Punctuation.DOUBLE_SQUARE_BRACKETS:
                    beginLine();
                    _code.Append("[[" + _syntax.getNewline());
                    break;
            }

            ++_indentations;
            _scopes.Add(new Scope(punc));
        }

        // End the current scope.
        public void endScope(bool semicolon = false, bool newline = true)
        {
            if (_scopes.Count == 0)
            {
                throw new ExceptionShaderGenError("End scope called with no scope active, please check your beginScope/endScope calls");
            }

            Syntax.Punctuation punc = _scopes[_scopes.Count - 1].punctuation;
            _scopes.RemoveAt(_scopes.Count - 1);
            --_indentations;

            switch (punc)
            {
                case Syntax.Punctuation.CURLY_BRACKETS:
                    beginLine();
                    _code.Append("}");
                    break;
                case Syntax.Punctuation.PARENTHESES:
                    beginLine();
                    _code.Append(")");
                    break;
                case Syntax.Punctuation.SQUARE_BRACKETS:
                    beginLine();
                    _code.Append("]");
                    break;
                case Syntax.Punctuation.DOUBLE_SQUARE_BRACKETS:
                    beginLine();
                    _code.Append("]]");
                    break;
            }
            if (semicolon)
            {
                _code.Append(";");
            }
            if (newline)
            {
                _code.Append(_syntax.getNewline());
            }
        }

        // Start a new line.
        public void beginLine()
        {
            for (int i = 0; i < _indentations; ++i)
            {
                _code.Append(_syntax.getIndentation());
            }
        }

        // End the current line.
        public void endLine(bool semicolon = true)
        {
            if (semicolon)
            {
                _code.Append(";");
            }
            newLine();
        }

        // Add a newline character.
        public void newLine()
        {
            _code.Append(_syntax.getNewline());
        }

        // Add a string.
        public void addString(string str)
        {
            _code.Append(str);
        }

        // Add a single line of code, optionally appending a semicolon.
        public void addLine(string str, bool semicolon = true)
        {
            beginLine();
            addString(str);
            endLine(semicolon);
        }

        // Add a single line code comment.
        public void addComment(string str)
        {
            beginLine();
            _code.Append(_syntax.getSingleLineComment() + str);
            endLine(false);
        }

        // Add a block of code.
        public void addBlock(string str, FilePath sourceFilename, GenContext context)
        {
            string INCLUDE = _syntax.getIncludeStatement();
            string QUOTE = _syntax.getStringQuote();

            // Add each line in the block seperately to get correct indentation.
            foreach (string line in getLines(str))
            {
                int pos = line.IndexOf(INCLUDE, StringComparison.Ordinal);
                if (pos != -1)
                {
                    int startQuote = line.IndexOfAny(QUOTE.ToCharArray());
                    int endQuote = line.LastIndexOfAny(QUOTE.ToCharArray());
                    if (startQuote != -1 && endQuote != -1 && endQuote > startQuote)
                    {
                        int length = (endQuote - startQuote) - 1;
                        if (length != 0)
                        {
                            string filename = line.Substring(startQuote + 1, length);
                            addInclude(new FilePath(filename), sourceFilename, context);
                        }
                    }
                }
                else
                {
                    addLine(line, false);
                }
            }
        }

        // Add the contents of an include file if not already present.
        public void addInclude(FilePath includeFilename, FilePath sourceFilename, GenContext context)
        {
            string modifiedFile = includeFilename.asString();
            tokenSubstitution(context.getShaderGenerator().getTokenSubstitutions(), ref modifiedFile);
            FilePath resolvedFile = context.resolveSourceFile(new FilePath(modifiedFile), sourceFilename.getParentPath());

            if (!_includes.Contains(resolvedFile.asString()))
            {
                string content = readFile(resolvedFile);
                if (content.Length == 0)
                {
                    throw new ExceptionShaderGenError("Could not find include file: '" + includeFilename.asString() + "'");
                }
                _includes.Add(resolvedFile.asString());
                addBlock(content, resolvedFile, context);
            }
        }

        // Return true if this stage depends on the given source file.
        public bool hasSourceDependency(FilePath file)
        {
            return _sourceDependencies.Contains(file.asString());
        }

        // Add a source file dependency for the given file.
        public void addSourceDependency(FilePath file)
        {
            _sourceDependencies.Add(file.asString());
        }

        // Add a value.
        public void addValue(string value)
        {
            _code.Append(value);
        }

        public void addValue(int value)
        {
            _code.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void addValue(float value)
        {
            // As a stream writes a float with its default formatting.
            using (ScopedFloatFormatting format = new ScopedFloatFormatting(Value.FloatFormat.FloatFormatDefault, 6))
            {
                _code.Append(toValueString(value));
            }
        }

        // Add the function definition for a node's implementation.
        public void addFunctionDefinition(ShaderNode node, GenContext context)
        {
            ShaderNodeImpl impl = node.getImplementation();
            ulong id = impl.getHash();

            // Make sure it's not already defined.
            if (!_definedFunctions.Contains(id))
            {
                _definedFunctions.Add(id);
                impl.emitFunctionDefinition(node, context, this);
            }
        }

        // Add the function call for the given node.
        // This will register the function as being called in the current scope, and code for the
        // function call will be added to the stage. If emitCode is set to false the code for the
        // function call will be omitted.
        public void addFunctionCall(ShaderNode node, GenContext context, bool emitCode = true)
        {
            // Register this function as being called in the current scope.
            ShaderNode id = node;
            _scopes[_scopes.Count - 1].functions.Add(id);

            // Emit code for the function call if not omitted.
            if (emitCode)
            {
                node.getImplementation().emitFunctionCall(node, context, this);
            }
        }

        // Return true if the function for the given node has been emitted in the current scope.
        public bool isEmitted(ShaderNode node, GenContext context)
        {
            ShaderNode id = node;
            foreach (Scope s in _scopes)
            {
                if (s.functions.Contains(id))
                {
                    return true;
                }
            }
            return false;
        }

        // Set stage function name.
        public void setFunctionName(string functionName)
        {
            _functionName = functionName;
        }

        // ShaderStage Private Methods

        // The lines of a text as std::getline gives them: split at each line
        // feed, with no empty last line after a final line feed.
        private static List<string> getLines(string str)
        {
            List<string> lines = new List<string>();
            int start = 0;
            while (start < str.Length)
            {
                int end = str.IndexOf('\n', start);
                if (end == -1)
                {
                    lines.Add(str.Substring(start));
                    break;
                }
                lines.Add(str.Substring(start, end - start));
                start = end + 1;
            }
            return lines;
        }

        // ShaderStage Private Members

        // Name of the stage
        private readonly string _name;

        // Stage function name.
        private string _functionName = "";

        // Syntax for the type of shader to generate.
        private Syntax _syntax;

        // Current indentation level.
        private int _indentations;

        // Current scope.
        private List<Scope> _scopes = new List<Scope>();

        // Set of include files that has been included.
        private StringSet _includes = new StringSet();

        // Set of source file dependencies from source code nodes
        private StringSet _sourceDependencies = new StringSet();

        // Set of hash ID's for functions that has been defined.
        private HashSet<ulong> _definedFunctions = new HashSet<ulong>();

        // Block holding constant variables for this stage.
        internal VariableBlock _constants;

        // Map of blocks holding uniform variables for this stage.
        internal Dictionary<string, VariableBlock> _uniforms = new Dictionary<string, VariableBlock>(StringComparer.Ordinal);

        // Map of blocks holding input variables for this stage.
        internal Dictionary<string, VariableBlock> _inputs = new Dictionary<string, VariableBlock>(StringComparer.Ordinal);

        // Map of blocks holding output variables for this stage.
        internal Dictionary<string, VariableBlock> _outputs = new Dictionary<string, VariableBlock>(StringComparer.Ordinal);

        // Resulting source code for this stage.
        private StringBuilder _code = new StringBuilder();
    }

    // ShaderStage Functions
    public static partial class Mx
    {
        // Utility function for adding a new shader port to a uniform block.
        public static ShaderPort addStageUniform(string block, TypeDesc type, string name, ShaderStage stage)
        {
            VariableBlock uniforms = stage.getUniformBlock(block);
            return uniforms.add(type, name);
        }

        // Utility function for adding a new shader port to an input block.
        public static ShaderPort addStageInput(string block, TypeDesc type, string name, ShaderStage stage, bool shouldWiden = false)
        {
            VariableBlock inputs = stage.getInputBlock(block);
            return inputs.add(type, name, null, shouldWiden);
        }

        // Utility function for adding a new shader port to an output block.
        public static ShaderPort addStageOutput(string block, TypeDesc type, string name, ShaderStage stage, bool shouldWiden = false)
        {
            VariableBlock outputs = stage.getOutputBlock(block);
            return outputs.add(type, name, null, shouldWiden);
        }

        // Utility function for adding a connector block between stages.
        public static void addStageConnectorBlock(string block, string instance, ShaderStage from, ShaderStage to)
        {
            from.createOutputBlock(block, instance);
            to.createInputBlock(block, instance);
        }

        // Utility function for adding a variable to a stage connector block.
        public static void addStageConnector(string block, TypeDesc type, string name, ShaderStage from, ShaderStage to, bool shouldWiden = false)
        {
            addStageOutput(block, type, name, from, shouldWiden);
            addStageInput(block, type, name, to, shouldWiden);
        }
    }
}
