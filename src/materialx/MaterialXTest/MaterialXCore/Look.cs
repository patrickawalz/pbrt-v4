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

public static class CoreLookTests
{
    [TEST("Core", "Look")]
    public static void Core_Look()
    {
        Document doc = createDocument();

        // Create a material and look.
        Node shaderNode = doc.addNode("standard_surface", "", SURFACE_SHADER_TYPE_STRING);
        Node materialNode = doc.addMaterialNode("", shaderNode);
        Look look = doc.addLook();

        // Bind the material to a geometry string.
        MaterialAssign matAssign1 = look.addMaterialAssign("matAssign1", materialNode.getName());
        matAssign1.setGeom("/robot1");
        REQUIRE(matAssign1.getReferencedMaterial() == materialNode);
        REQUIRE(getGeometryBindings(materialNode, "/robot1").Count == 1);
        REQUIRE(getGeometryBindings(materialNode, "/robot2").Count == 0);

        // Bind the material to a geometric collection.
        MaterialAssign matAssign2 = look.addMaterialAssign("matAssign2", materialNode.getName());
        Collection collection = doc.addCollection();
        collection.setIncludeGeom("/robot2");
        collection.setExcludeGeom("/robot2/left_arm");
        matAssign2.setCollection(collection);
        REQUIRE(getGeometryBindings(materialNode, "/robot2").Count == 1);
        REQUIRE(getGeometryBindings(materialNode, "/robot2/right_arm").Count == 1);
        REQUIRE(getGeometryBindings(materialNode, "/robot2/left_arm").Count == 0);

        // Create a property assignment.
        PropertyAssign propertyAssign = look.addPropertyAssign();
    	propertyAssign.setProperty("twosided");
        propertyAssign.setGeom("/robot1");
        propertyAssign.setValue(true);
        REQUIRE(propertyAssign.getProperty() == "twosided");
        REQUIRE(propertyAssign.getGeom() == "/robot1");
        REQUIRE(propertyAssign.getValue().isA<bool>());
        REQUIRE(propertyAssign.getValue().asA<bool>() == true);

        // Create a property set assignment.
        PropertySet propertySet = doc.addPropertySet();
        propertySet.setPropertyValue("matte", false);
        REQUIRE(propertySet.getPropertyValue("matte").isA<bool>());
        REQUIRE(propertySet.getPropertyValue("matte").asA<bool>() == false);
        PropertySetAssign propertySetAssign = look.addPropertySetAssign();
    	propertySetAssign.setPropertySet(propertySet);
        propertySetAssign.setGeom("/robot1");
        REQUIRE(propertySetAssign.getPropertySet() == propertySet);
        REQUIRE(propertySetAssign.getGeom() == "/robot1");

        // Create a variant set.
        VariantSet variantSet = doc.addVariantSet("damageVars");
        variantSet.addVariant("original");
        variantSet.addVariant("damaged");
        REQUIRE(variantSet.getVariants().Count == 2);

        // Create a visibility element.
        Visibility visibility = look.addVisibility();
        REQUIRE(visibility.getVisible() == false);
        visibility.setVisible(true);
        REQUIRE(visibility.getVisible() == true);
        visibility.setGeom("/robot2");
        REQUIRE(visibility.getGeom() == "/robot2");
        visibility.setCollection(collection);
        REQUIRE(visibility.getCollection() == collection);

        // Create an inherited look.
        Look look2 = doc.addLook();
        look2.setInheritsFrom(look);
        REQUIRE(look2.getActiveMaterialAssigns().Count == 2);
        REQUIRE(look2.getActivePropertyAssigns().Count == 1);
        REQUIRE(look2.getActivePropertySetAssigns().Count == 1);
        REQUIRE(look2.getActiveVisibilities().Count == 1);

        // Create and detect an inheritance cycle.
        look.setInheritsFrom(look2);
        REQUIRE(!doc.validate());
        look.setInheritsFrom(null);
        REQUIRE(doc.validate());

        // Disconnect the inherited look.
        look2.setInheritsFrom(null);
        REQUIRE(look2.getActiveMaterialAssigns().Count == 0);
        REQUIRE(look2.getActivePropertyAssigns().Count == 0);
        REQUIRE(look2.getActivePropertySetAssigns().Count == 0);
        REQUIRE(look2.getActiveVisibilities().Count == 0);
    }

    [TEST("Core", "LookGroup")]
    public static void Core_LookGroup()
    {
        Document doc = createDocument();

        LookGroup lookGroup = doc.addLookGroup("lookgroup1");
        List<LookGroup> lookGroups = doc.getLookGroups();
        REQUIRE(lookGroups.Count == 1);

        string looks = "look1,look2,look3,look4,look5";
        List<string> looksVec = splitString(looks, ",");
        foreach (string lookName in looksVec)
        {
            Look look = doc.addLook(lookName);
            REQUIRE(look != null);
        }
        lookGroup.setLooks(looks);

        string looks2 = lookGroup.getLooks();
        List<string> looksVec2 = splitString(looks2, ",");
        REQUIRE(looksVec.Count == looksVec2.Count);

        REQUIRE(lookGroup.getActiveLook().Length == 0);
        lookGroup.setActiveLook("look1");
        REQUIRE(lookGroup.getActiveLook() == "look1");

        doc.removeLookGroup("lookgroup1");
        lookGroups = doc.getLookGroups();
        REQUIRE(lookGroups.Count == 0);
    }
}
