// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/ShaderGraphRefactor.h and ShaderGraphRefactor.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // ShaderGraphRefactor Definition
    // Base class for shader graph refactoring passes.
    // Each pass identifies a structural pattern in the shader graph and
    // rewrites it into a mathematically equivalent form better suited
    // to the target backend.
    public abstract class ShaderGraphRefactor
    {
        // ShaderGraphRefactor Public Methods
        // Return the name of this refactoring pass.
        public abstract string getName();

        // Execute the pass on the given graph.
        // Return the number of graph edits made, or zero if
        // the pass is not applicable to the current context.
        public abstract int execute(ShaderGraph graph, GenContext context);
    }

    // NodeElisionRefactor Definition
    // Removes constant and dot nodes by bypassing them.
    // Constant nodes have their values moved downstream, and
    // dot nodes with filename-typed inputs are elided to prevent
    // extra samplers.
    public class NodeElisionRefactor : ShaderGraphRefactor
    {
        // NodeElisionRefactor Public Methods
        public override string getName()
        {
            string name = "nodeElision";
            return name;
        }

        public override int execute(ShaderGraph graph, GenContext context)
        {
            int numEdits = 0;
            foreach (ShaderNode node in graph.getNodes())
            {
                if (node.hasClassification(ShaderNode.Classification.CONSTANT))
                {
                    if (node.numInputs() != 1 || node.numOutputs() != 1)
                    {
                        // Constant node doesn't follow expected interface, cannot elide.
                        continue;
                    }
                    // Constant nodes can be elided by moving their value downstream.
                    bool canElide = context.getOptions().elideConstantNodes;
                    if (!canElide)
                    {
                        // We always elide filename constant nodes regardless of the
                        // option. See DOT below.
                        ShaderInput @in = node.getInput("value");
                        if (@in != null && @in.getType() == Type.FILENAME)
                        {
                            canElide = true;
                        }
                    }
                    if (canElide)
                    {
                        graph.bypass(node, 0);
                        ++numEdits;
                    }
                }
                else if (node.hasClassification(ShaderNode.Classification.DOT))
                {
                    if (node.numOutputs() != 1)
                    {
                        // Dot node doesn't follow expected interface, cannot elide.
                        continue;
                    }
                    // Filename dot nodes must be elided so they do not create extra samplers.
                    ShaderInput @in = node.getInput("in");
                    if (@in != null && @in.getType() == Type.FILENAME)
                    {
                        graph.bypass(node, 0);
                        ++numEdits;
                    }
                }
            }
            return numEdits;
        }
    }

    // PremultipliedBsdfAddRefactor Definition
    // Replaces BSDF mix nodes with premultiplied add nodes.
    // Transforms mix(A, B, w) into add(A*w, B*(1-w)) by folding
    // the mix weight into each BSDF's weight input, enabling
    // hardware shading languages to skip BSDF evaluation when
    // the weight is zero.
    public class PremultipliedBsdfAddRefactor : ShaderGraphRefactor
    {
        // PremultipliedBsdfAddRefactor Public Methods
        public override string getName()
        {
            string name = "premultipliedBsdfAdd";
            return name;
        }

        public override int execute(ShaderGraph graph, GenContext context)
        {
            if (!context.getOptions().premultipliedBsdfAdd)
            {
                return 0;
            }

            // Look up all required node definitions up front.
            Document doc = graph.getDocument();
            NodeDef invertDef = doc.getNodeDef("ND_invert_float");
            NodeDef mulFloatDef = doc.getNodeDef("ND_multiply_float");
            NodeDef mulBsdfDef = doc.getNodeDef("ND_multiply_bsdfF");
            NodeDef addBsdfDef = doc.getNodeDef("ND_add_bsdf");
            if (invertDef == null || mulFloatDef == null || mulBsdfDef == null || addBsdfDef == null)
            {
                return 0;
            }

            // Collect mix nodes with connected weights (avoid modifying the graph while iterating).
            List<ShaderNode> mixNodes = new List<ShaderNode>();
            foreach (ShaderNode node in graph.getNodes())
            {
                if (isBsdfMixNode(node))
                {
                    ShaderInput mix = node.getInput("mix");
                    if (mix != null && mix.getConnection() != null)
                    {
                        mixNodes.Add(node);
                    }
                }
            }

            int numEdits = 0;

            foreach (ShaderNode mixNode in mixNodes)
            {
                ShaderInput fgInput = mixNode.getInput("fg");
                ShaderInput bgInput = mixNode.getInput("bg");
                ShaderInput mixInput = mixNode.getInput("mix");
                ShaderOutput mixOutput = mixNode.getOutput();

                if (fgInput == null || bgInput == null || mixInput == null || mixOutput == null)
                {
                    continue;
                }

                ShaderOutput mixWeightSource = mixInput.getConnection();

                // Create an invert node to compute (1 - mix).
                string invertName = mixNode.getName() + "_mix_inv";
                ShaderNode invertNode = graph.createNode(invertName, invertName, invertDef, context);
                ShaderInput invertIn = invertNode.getInput("in");
                if (invertIn != null)
                {
                    invertIn.makeConnection(mixWeightSource);
                }
                ShaderOutput invertOutput = invertNode.getOutput();

                // Fold mix weights into each BSDF side.
                string namePrefix = mixNode.getName();
                ShaderOutput fgUpstream = foldWeightIntoBsdf(graph, context, fgInput, mixWeightSource,
                                                              namePrefix + "_fg", mulFloatDef, mulBsdfDef);
                ShaderOutput bgUpstream = foldWeightIntoBsdf(graph, context, bgInput, invertOutput,
                                                              namePrefix + "_bg", mulFloatDef, mulBsdfDef);

                // Create an add node to replace the mix.
                string addName = mixNode.getName() + "_add";
                ShaderNode addNode = graph.createNode(addName, addName, addBsdfDef, context);
                ShaderInput addIn1 = addNode.getInput("in1");
                ShaderInput addIn2 = addNode.getInput("in2");
                if (addIn1 == null || addIn2 == null)
                {
                    continue;
                }

                if (fgUpstream != null)
                {
                    addIn1.makeConnection(fgUpstream);
                }
                if (bgUpstream != null)
                {
                    addIn2.makeConnection(bgUpstream);
                }

                // Rewire all downstream connections from the mix output to the add output.
                graph.replaceOutput(mixOutput, addNode.getOutput());

                // Disconnect the mix node's inputs.
                fgInput.breakConnection();
                bgInput.breakConnection();
                mixInput.breakConnection();

                ++numEdits;
            }

            return numEdits;
        }
    }

    // DistributeLayerOverMixRefactor Definition
    // Distributes layer operations over mix nodes.
    // Transforms layer(mix(A, B, w), C) into mix(layer(A, C), layer(B, C), w)
    // to satisfy backends that cannot handle a mixed BSDF as the top
    // operand of a layer node.
    public class DistributeLayerOverMixRefactor : ShaderGraphRefactor
    {
        // DistributeLayerOverMixRefactor Public Methods
        public override string getName()
        {
            string name = "distributeLayerOverMix";
            return name;
        }

        public override int execute(ShaderGraph graph, GenContext context)
        {
            if (!context.getOptions().distributeLayerOverBsdfMix)
            {
                return 0;
            }

            // Look up all required node definitions up front.
            Document doc = graph.getDocument();
            NodeDef layerBsdfDef = doc.getNodeDef("ND_layer_bsdf");
            NodeDef mixBsdfDef = doc.getNodeDef("ND_mix_bsdf");
            if (layerBsdfDef == null || mixBsdfDef == null)
            {
                return 0;
            }

            // Collect layer nodes to process (avoid modifying the graph while iterating).
            List<ShaderNode> layerNodes = new List<ShaderNode>();
            foreach (ShaderNode node in graph.getNodes())
            {
                if (isLayerWithMixTop(node))
                {
                    layerNodes.Add(node);
                }
            }

            int numEdits = 0;

            foreach (ShaderNode layerNode in layerNodes)
            {
                ShaderInput topInput = layerNode.getInput("top");
                ShaderInput baseInput = layerNode.getInput("base");
                ShaderOutput layerOutput = layerNode.getOutput();

                if (topInput == null || baseInput == null || layerOutput == null)
                {
                    continue;
                }

                ShaderOutput topConnection = topInput.getConnection();
                if (topConnection == null)
                {
                    continue;
                }

                ShaderNode mixNode = topConnection.getNode();
                ShaderInput fgInput = mixNode.getInput("fg");
                ShaderInput bgInput = mixNode.getInput("bg");
                ShaderInput mixInput = mixNode.getInput("mix");

                if (fgInput == null || bgInput == null || mixInput == null)
                {
                    continue;
                }

                ShaderOutput fgUpstream = fgInput.getConnection();
                ShaderOutput bgUpstream = bgInput.getConnection();
                ShaderOutput baseUpstream = baseInput.getConnection();
                ShaderOutput mixWeightSource = mixInput.getConnection();

                // Create layer(fg, base).
                string layer1Name = layerNode.getName() + "_tf";
                ShaderNode layer1 = graph.createNode(layer1Name, layer1Name, layerBsdfDef, context);
                ShaderInput layer1Top = layer1.getInput("top");
                ShaderInput layer1Base = layer1.getInput("base");
                if (layer1Top != null && fgUpstream != null)
                {
                    layer1Top.makeConnection(fgUpstream);
                }
                if (layer1Base != null && baseUpstream != null)
                {
                    layer1Base.makeConnection(baseUpstream);
                }

                // Create layer(bg, base).
                string layer2Name = layerNode.getName() + "_notf";
                ShaderNode layer2 = graph.createNode(layer2Name, layer2Name, layerBsdfDef, context);
                ShaderInput layer2Top = layer2.getInput("top");
                ShaderInput layer2Base = layer2.getInput("base");
                if (layer2Top != null && bgUpstream != null)
                {
                    layer2Top.makeConnection(bgUpstream);
                }
                if (layer2Base != null && baseUpstream != null)
                {
                    layer2Base.makeConnection(baseUpstream);
                }

                // Create mix(layer1, layer2, w).
                string newMixName = layerNode.getName();
                ShaderNode newMixNode = graph.createNode(newMixName + "_mix", newMixName + "_mix", mixBsdfDef, context);
                ShaderInput newFg = newMixNode.getInput("fg");
                ShaderInput newBg = newMixNode.getInput("bg");
                ShaderInput newMix = newMixNode.getInput("mix");
                if (newFg != null)
                {
                    newFg.makeConnection(layer1.getOutput());
                }
                if (newBg != null)
                {
                    newBg.makeConnection(layer2.getOutput());
                }
                if (newMix != null && mixWeightSource != null)
                {
                    newMix.makeConnection(mixWeightSource);
                }
                else if (newMix != null && mixInput.getValue() != null)
                {
                    newMix.setValue(mixInput.getValue());
                }

                // Rewire downstream connections from the old layer to the new mix.
                graph.replaceOutput(layerOutput, newMixNode.getOutput());

                // Disconnect the old layer node's inputs.
                topInput.breakConnection();
                baseInput.breakConnection();

                ++numEdits;
            }

            return numEdits;
        }
    }

    // ShaderGraphRefactor Functions
    public static partial class Mx
    {
        internal static bool isBsdfMixNode(ShaderNode node)
        {
            return node.hasClassification(ShaderNode.Classification.BSDF |
                                           ShaderNode.Classification.CLOSURE |
                                           ShaderNode.Classification.MIX);
        }

        internal static bool isLayerWithMixTop(ShaderNode node)
        {
            if (!node.hasClassification(ShaderNode.Classification.LAYER))
            {
                return false;
            }
            ShaderInput top = node.getInput("top");
            if (top == null || top.getConnection() == null)
            {
                return false;
            }
            return isBsdfMixNode(top.getConnection().getNode());
        }

        // Fold a mix weight into one side of a BSDF mix node, inserting a multiply
        // node to combine the existing weight (connected or constant) with the mix
        // weight.  Return the upstream output to connect to the replacement add node.
        internal static ShaderOutput foldWeightIntoBsdf(ShaderGraph graph, GenContext context, ShaderInput bsdfInput, ShaderOutput weightSource, string namePrefix, NodeDef mulFloatDef, NodeDef mulBsdfDef)
        {
            ShaderOutput bsdfUpstream = bsdfInput.getConnection();
            if (bsdfUpstream == null)
            {
                return null;
            }

            ShaderNode bsdfNode = bsdfUpstream.getNode();
            ShaderInput weightInput = bsdfNode.getInput("weight");
            if (weightInput != null)
            {
                // Create a multiply node to combine existing weight with mix weight.
                string mulName = namePrefix + "_weight";
                ShaderNode mulNode = graph.createNode(mulName, mulName, mulFloatDef, context);
                ShaderInput mulIn1 = mulNode.getInput("in1");
                ShaderInput mulIn2 = mulNode.getInput("in2");
                if (mulIn1 != null && mulIn2 != null)
                {
                    ShaderOutput existingSource = weightInput.getConnection();
                    if (existingSource != null)
                    {
                        weightInput.breakConnection();
                        mulIn1.makeConnection(existingSource);
                    }
                    else if (weightInput.getValue() != null)
                    {
                        mulIn1.setValue(weightInput.getValue());
                    }
                    else
                    {
                        mulIn1.setValue(Value.createValue<float>(1.0f));
                    }
                    mulIn2.makeConnection(weightSource);
                    weightInput.makeConnection(mulNode.getOutput());
                }
                return bsdfUpstream;
            }
            else
            {
                // Wrap the BSDF with a BSDF*float multiply when no weight input exists.
                string mulName = namePrefix + "_mul";
                ShaderNode mulNode = graph.createNode(mulName, mulName, mulBsdfDef, context);
                ShaderInput mulIn1 = mulNode.getInput("in1");
                ShaderInput mulIn2 = mulNode.getInput("in2");
                if (mulIn1 != null && mulIn2 != null)
                {
                    mulIn1.makeConnection(bsdfUpstream);
                    mulIn2.makeConnection(weightSource);
                    return mulNode.getOutput();
                }
                return bsdfUpstream;
            }
        }
    }
}
