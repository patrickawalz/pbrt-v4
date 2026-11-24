//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.Text;
using MaterialX;
using testing;
using static MaterialX.Mx;
using static MaterialXTest.Catch;

public static class CoreMaterialTests
{
    [TEST("Core", "Material")]
    public static void Core_Material()
    {
        Document doc = createDocument();

        // Create a base shader nodedef.
        NodeDef simpleSrf = doc.addNodeDef("ND_simpleSrf", SURFACE_SHADER_TYPE_STRING, "simpleSrf");
        simpleSrf.setInputValue("diffColor", new Color3(1.0f));
        simpleSrf.setInputValue("specColor", new Color3(0.0f));
        simpleSrf.setInputValue("roughness", 0.25f);
        simpleSrf.setTokenValue("texId", "01");
        REQUIRE(simpleSrf.getInputValue("diffColor").asA<Color3>() == new Color3(1.0f));
        REQUIRE(simpleSrf.getInputValue("specColor").asA<Color3>() == new Color3(0.0f));
        REQUIRE(simpleSrf.getInputValue("roughness").asA<float>() == 0.25f);
        REQUIRE(simpleSrf.getTokenValue("texId") == "01");

        // Create an inherited shader nodedef.
        NodeDef anisoSrf = doc.addNodeDef("ND_anisoSrf", SURFACE_SHADER_TYPE_STRING, "anisoSrf");
        anisoSrf.setInheritsFrom(simpleSrf);
        anisoSrf.setInputValue("anisotropy", 0.0f);
        REQUIRE(anisoSrf.getInheritsFrom() == simpleSrf);

        // Instantiate shader and material nodes.
        Node shaderNode = doc.addNode(anisoSrf.getNodeString(), "", anisoSrf.getType());
        Node materialNode = doc.addMaterialNode("", shaderNode);
        REQUIRE(materialNode.getUpstreamElement() == shaderNode);
        REQUIRE(shaderNode.getNodeDef() == anisoSrf);

        // Set nodedef and shader node qualifiers.
        shaderNode.setVersionString("2.0");
        REQUIRE(shaderNode.getNodeDef() == null);
        anisoSrf.setVersionString("2");
        shaderNode.setVersionString("2");
        REQUIRE(shaderNode.getNodeDef() == anisoSrf);
        shaderNode.setType(VOLUME_SHADER_TYPE_STRING);
        REQUIRE(shaderNode.getNodeDef() == null);
        shaderNode.setType(SURFACE_SHADER_TYPE_STRING);
        REQUIRE(shaderNode.getNodeDef() == anisoSrf);

        // Bind a shader input to a value.
        Input instanceSpecColor = shaderNode.setInputValue("specColor", new Color3(1.0f));
        REQUIRE(instanceSpecColor.getValue().asA<Color3>() == new Color3(1.0f));
        REQUIRE(instanceSpecColor.getDefaultValue().asA<Color3>() == new Color3(0.0f));
        REQUIRE(doc.validate());
    }

    [TEST("Core", "MaterialDiscovery")]
    public static void Core_MaterialDiscovery()
    {
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document doc = createDocument();
        readFromXmlFile(doc, "resources/Materials/TestSuite/stdlib/materials/material_node_discovery.mtlx", searchPath);

        // 1. Find all materials referenced by material assignments
        //    which are found in connected nodegraphs
        HashSet<Element> foundNodes = new HashSet<Element>();
        foreach (Look look in doc.getLooks())
        {
            foreach (MaterialAssign materialAssign in look.getMaterialAssigns())
            {
                foreach (Output assignOutput in materialAssign.getMaterialOutputs())
                {
                    Node assignNode = assignOutput.getConnectedNode();
                    if (assignNode != null)
                    {
                        foundNodes.Add(assignNode);
                    }
                }
            }
        }
        CHECK(foundNodes.Count == 1);

        // 2. Nodegraph Test: Find all graphs with material nodes exposed as outputs.
        //    
        foundNodes.Clear();
        foreach (NodeGraph nodeGraph in doc.getNodeGraphs())
        {
            foreach (Output nodeGraphOutput in nodeGraph.getMaterialOutputs())
            {
                Node nodeGraphNode = nodeGraphOutput.getConnectedNode();
                if (nodeGraphNode != null)
                {
                    foundNodes.Add(nodeGraphNode);
                }
            }
        }
        CHECK(foundNodes.Count == 3);

        // 3. Document test: Find all material nodes within nodegraphs
        //    which are not implementations. This will return less nodes
        //    as implementation graphs exist in the document.
        foundNodes.Clear();
        if (doc != null)
        {
            foreach (Output documentOutput in doc.getMaterialOutputs())
            {
                Node documentNode = documentOutput.getConnectedNode();
                if (documentNode != null)
                {
                    foundNodes.Add(documentNode);
                }
            }
        }
        CHECK(foundNodes.Count == 2);
    }
}
