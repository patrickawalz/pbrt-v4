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

public static class CoreGeomTests
{
    [TEST("Core", "GeomStrings")]
    public static void Core_GeomStrings()
    {
        // Test conversion between geometry strings and paths.
        List<string> geomStrings = new List<string>
        {
            "",
            "/",
            "/robot1",
            "/robot1/left_arm"
        };
        foreach (string geomString in geomStrings)
        {
            GeomPath geomPath = new GeomPath(geomString);
            string newGeomString = geomPath.asString();
            REQUIRE(newGeomString == geomString);
        }

        // Test for overlapping paths.
        REQUIRE(geomStringsMatch("/", "/robot1"));
        REQUIRE(geomStringsMatch("/robot1", "/robot1/left_arm"));
        REQUIRE(geomStringsMatch("/robot1, /robot2", "/robot2/left_arm"));
        REQUIRE(!geomStringsMatch("", "/robot1"));
        REQUIRE(!geomStringsMatch("/robot1", "/robot2"));
        REQUIRE(!geomStringsMatch("/robot1, /robot2", "/robot3"));

        // Test that one path contains another.
        REQUIRE(geomStringsMatch("/", "/robot1", true));
        REQUIRE(!geomStringsMatch("/robot1", "/", true));
    }

    [TEST("Core", "GeomElements")]
    public static void Core_GeomElements()
    {
        Document doc = createDocument();

        // Add geominfos and tokens
        GeomInfo geominfo1 = doc.addGeomInfo("geominfo1", "/robot1, /robot2");
        geominfo1.setTokenValue("asset", "robot");
        GeomInfo geominfo2 = doc.addGeomInfo("geominfo2", "/robot1");
        geominfo2.setTokenValue("id", "01");
        GeomInfo geominfo3 = doc.addGeomInfo("geominfo3", "/robot2");
        geominfo3.setTokenValue("id", "02");
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { doc.addGeomInfo("geominfo1"); });

        // Create a node graph with a single image node.
        NodeGraph nodeGraph = doc.addNodeGraph();
        nodeGraph.setFilePrefix("folder/");
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { doc.addNodeGraph(nodeGraph.getName()); });
        Node image = nodeGraph.addNode("image");
        image.setInputValue("file", "<asset><id>_diffuse_<UDIM>.tif", FILENAME_TYPE_STRING);

        // Test filename string substitutions.
        Input fileInput = image.getInput("file");
        StringResolver resolver1 = image.createStringResolver("/robot1");
        resolver1.setUdimString("1001");
        StringResolver resolver2 = image.createStringResolver("/robot2");
        resolver2.setUdimString("1002");
        REQUIRE(fileInput.getResolvedValue(resolver1).asA<string>() == "folder/robot01_diffuse_1001.tif");
        REQUIRE(fileInput.getResolvedValue(resolver2).asA<string>() == "folder/robot02_diffuse_1002.tif");

        // Create a geominfo with an attribute.
        GeomInfo geominfo4 = doc.addGeomInfo("geominfo4", "/robot1");
        List<string> udimSet = new List<string> { "1001", "1002", "1003", "1004" };
        geominfo4.setGeomPropValue(UDIM_SET_PROPERTY, udimSet);
        REQUIRE(vectorEqual(doc.getGeomPropValue(UDIM_SET_PROPERTY, "/robot1").asA<List<string>>(), udimSet));
        REQUIRE(doc.getGeomPropValue(UDIM_SET_PROPERTY, "/robot2") == null);

        // Create a base collection.
        Collection collection1 = doc.addCollection("collection1");
        collection1.setIncludeGeom("/scene1");
        collection1.setExcludeGeom("/scene1/sphere2");
        REQUIRE(collection1.matchesGeomString("/scene1/sphere1"));
        REQUIRE(!collection1.matchesGeomString("/scene1/sphere2"));

        // Create a derived collection.
        Collection collection2 = doc.addCollection("collection2");
        collection2.setIncludeCollection(collection1);
        REQUIRE(collection2.matchesGeomString("/scene1/sphere1"));
        REQUIRE(!collection2.matchesGeomString("/scene1/sphere2"));

        // Create and test an include cycle.
        collection1.setIncludeCollection(collection2);
        REQUIRE(!doc.validate());
        collection1.setIncludeCollection(null);
        REQUIRE(doc.validate());

        // Test geometry string substitutions.
        collection1.setGeomPrefix("/root");
        REQUIRE(collection1.matchesGeomString("/root/scene1"));
        REQUIRE(!collection1.matchesGeomString("/root/scene2"));
    }

    [TEST("Core", "GeomPropDef")]
    public static void Core_GeomPropDef()
    {
        Document doc = createDocument();

        // Declare a GeomPropDef for world-space normal.
        GeomPropDef worldNormal = doc.addGeomPropDef("Nworld", "normal");
        worldNormal.setSpace("world");

        // Create a NodeDef with an input that defaults to the declared world-space
        // normal property.
        NodeDef nodedef = doc.addNodeDef("ND_foo", "color3", "foo");
        Input input = nodedef.addInput("input1", "vector3");
        input.setDefaultGeomPropString(worldNormal.getName());

        // Validate connections.
        REQUIRE(input.getDefaultGeomProp() == worldNormal);
        REQUIRE(doc.validate());
    }
}
