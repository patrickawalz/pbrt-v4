// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/GenContext.h and GenContext.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // GenContext Definition
    // A context class for shader generation.
    // Used for thread local storage of data needed during shader generation.
    public class GenContext
    {
        // GenContext Public Methods
        // Constructor.
        public GenContext(ShaderGenerator sg)
        {
            _sg = sg;

            if (_sg == null)
            {
                throw new ExceptionShaderGenError("GenContext must have a valid shader generator");
            }

            // Apply the generator's default options for its target.
            _sg.applyDefaultOptions(_options);

            // Collect and cache reserved words from the shader generator
            StringSet reservedWords = new StringSet();

            // Add reserved words from the syntax
            reservedWords = _sg.getSyntax().getReservedWords();

            // Add token substitution identifiers
            foreach (KeyValuePair<string, string> it in _sg.getTokenSubstitutions())
            {
                if (it.Value.Length != 0)
                {
                    reservedWords.Add(it.Value);
                }
            }

            addReservedWords(reservedWords);

            _applicationVariableHandler = null;
        }

        // Return shader generatior.
        public ShaderGenerator getShaderGenerator()
        {
            return _sg;
        }

        // Return shader generation options.
        public GenOptions getOptions()
        {
            return _options;
        }

        // Return a TypeDesc for the given type name.
        public TypeDesc getTypeDesc(string name)
        {
            return _sg.getTypeSystem().getType(name);
        }

        // Register a user search path for finding source code during
        // code generation.
        public void registerSourceCodeSearchPath(FilePath path)
        {
            _sourceCodeSearchPath.append(path);
        }

        // Register a user search path for finding source code during
        // code generation.
        public void registerSourceCodeSearchPath(FileSearchPath path)
        {
            _sourceCodeSearchPath.append(path);
        }

        // Resolve a source code filename, first checking the given local path
        // then checking any file paths registered by the user.
        public FilePath resolveSourceFile(FilePath filename, FilePath localPath)
        {
            FileSearchPath searchPath = _sourceCodeSearchPath;
            if (!localPath.isEmpty())
            {
                searchPath.prepend(localPath);
            }
            return searchPath.find(filename).getNormalized();
        }

        // Add reserved words that should not be used as
        // identifiers during code generation.
        public void addReservedWords(StringSet names)
        {
            _reservedWords.UnionWith(names);
        }

        // Return the set of reserved words that should not be used
        // as identifiers during code generation.
        public StringSet getReservedWords()
        {
            return _reservedWords;
        }

        // Cache a shader node implementation.
        public void addNodeImplementation(string name, ShaderNodeImpl impl)
        {
            _nodeImpls[name] = impl;
        }

        // Find and return a cached shader node implementation,
        // or return nullptr if no implementation is found.
        public ShaderNodeImpl findNodeImplementation(string name)
        {
            ShaderNodeImpl found;
            return _nodeImpls.TryGetValue(name, out found) ? found : null;
        }

        // Get the names of all cached node implementations.
        public void getNodeImplementationNames(StringSet names)
        {
            foreach (KeyValuePair<string, ShaderNodeImpl> it in _nodeImpls)
            {
                names.Add(it.Key);
            }
        }

        // Clear all cached shader node implementation.
        public void clearNodeImplementations()
        {
            _nodeImpls.Clear();
        }

        // Push a parent node onto the stack
        public void pushParentNode(Node node)
        {
            _parentNodes.Add(node);
        }

        // Pop the current parent node from the stack.
        public void popParentNode()
        {
            _parentNodes.RemoveAt(_parentNodes.Count - 1);
        }

        // Return the current stack of parent nodes.
        public List<Node> getParentNodes()
        {
            return _parentNodes;
        }

        // Add user data to the context to make it
        // available during shader generator.
        public void pushUserData(string name, GenUserData data)
        {
            List<GenUserData> found;
            if (_userData.TryGetValue(name, out found))
            {
                found.Add(data);
            }
            else
            {
                List<GenUserData> list = new List<GenUserData>();
                list.Add(data);
                _userData[name] = list;
            }
        }

        // Remove user data from the context.
        public void popUserData(string name)
        {
            List<GenUserData> found;
            if (_userData.TryGetValue(name, out found))
            {
                found.RemoveAt(found.Count - 1);
            }
        }

        // Clear all user data from the context.
        public void clearUserData()
        {
            _userData.Clear();
        }

        // GenContext Public Types
        // Return user data with given name,
        // or nullptr if no data is found.
        public T getUserData<T>(string name) where T : GenUserData
        {
            List<GenUserData> found;
            return _userData.TryGetValue(name, out found) && found.Count != 0 ? found[found.Count - 1].asA<T>() : null;
        }

        // GenContext Public Methods
        // Add an input suffix to be used for the input in this context.
        // @param input Node input
        // @param suffix Suffix string
        public void addInputSuffix(ShaderInput input, string suffix)
        {
            _inputSuffix[input] = suffix;
        }

        // Remove an input suffix to be used for the input in this context.
        // @param input Node input
        public void removeInputSuffix(ShaderInput input)
        {
            _inputSuffix.Remove(input);
        }

        // Get an input suffix to be used for the input in this context.
        // @param input Node input
        // @param suffix Suffix string returned. Is empty if not found.
        public void getInputSuffix(ShaderInput input, ref string suffix)
        {
            suffix = "";
            string iterFound;
            if (_inputSuffix.TryGetValue(input, out iterFound))
            {
                suffix = iterFound;
            }
        }

        // Add an output suffix to be used for the output in this context.
        // @param output Node output
        // @param suffix Suffix string
        public void addOutputSuffix(ShaderOutput output, string suffix)
        {
            _outputSuffix[output] = suffix;
        }

        // Remove an output suffix to be used for the output in this context.
        // @param output Node output
        public void removeOutputSuffix(ShaderOutput output)
        {
            _outputSuffix.Remove(output);
        }

        // Get an output suffix to be used for the output in this context.
        // @param output Node output
        // @param suffix Suffix string returned. Is empty if not found.
        public void getOutputSuffix(ShaderOutput output, ref string suffix)
        {
            suffix = "";
            string iterFound;
            if (_outputSuffix.TryGetValue(output, out iterFound))
            {
                suffix = iterFound;
            }
        }

        // Set handler for application variables
        public void setApplicationVariableHandler(Action<ShaderNode, GenContext> handler)
        {
            _applicationVariableHandler = handler;
        }

        // Get handler for application variables
        public Action<ShaderNode, GenContext> getApplicationVariableHandler()
        {
            return _applicationVariableHandler;
        }

        // GenContext Protected Members
        protected ShaderGenerator _sg;

        protected GenOptions _options = new GenOptions();

        protected FileSearchPath _sourceCodeSearchPath = new FileSearchPath();

        protected StringSet _reservedWords = new StringSet();

        protected Dictionary<string, ShaderNodeImpl> _nodeImpls = new Dictionary<string, ShaderNodeImpl>();

        protected Dictionary<string, List<GenUserData>> _userData = new Dictionary<string, List<GenUserData>>();

        protected Dictionary<ShaderInput, string> _inputSuffix = new Dictionary<ShaderInput, string>();

        protected Dictionary<ShaderOutput, string> _outputSuffix = new Dictionary<ShaderOutput, string>();

        protected List<Node> _parentNodes = new List<Node>();

        protected Action<ShaderNode, GenContext> _applicationVariableHandler;
    }

    // ScopedSetVariableName Definition
    // A RAII class for overriding port variable names.
    public class ScopedSetVariableName : IDisposable
    {
        // ScopedSetVariableName Public Methods

        // Constructor for setting a new variable name for a port.
        public ScopedSetVariableName(string name, ShaderPort port)
        {
            _port = port;
            _oldName = port.getVariable();

            _port.setVariable(name);
        }

        // Destructor restoring the original variable name.
        ~ScopedSetVariableName()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // ScopedSetVariableName Protected Methods
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                _port.setVariable(_oldName);
            }
            _disposed = true;
        }

        // ScopedSetVariableName Private Members
        private ShaderPort _port;
        private string _oldName = "";
        private bool _disposed;
    }
}
