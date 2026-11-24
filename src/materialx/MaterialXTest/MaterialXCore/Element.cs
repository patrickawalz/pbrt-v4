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

public static class CoreElementTests
{
    [TEST("Core", "Element")]
    public static void Core_Element()
    {
        // Create a document.
        Document doc = createDocument();

        // Create elements
        Element elem1 = doc.addChildOfCategory("generic");
        Element elem2 = doc.addChildOfCategory("generic");
        REQUIRE(elem1.getParent() == doc);
        REQUIRE(elem2.getParent() == doc);
        REQUIRE(elem1.getRoot() == doc);
        REQUIRE(elem2.getRoot() == doc);
        REQUIRE(doc.getChildren()[0] == elem1);
        REQUIRE(doc.getChildren()[1] == elem2);

        // Set hierarchical properties
        doc.setFilePrefix("folder/");
        doc.setColorSpace("lin_rec709_scene");
        REQUIRE(elem1.getActiveFilePrefix() == doc.getFilePrefix());
        REQUIRE(elem2.getActiveColorSpace() == doc.getColorSpace());

        // Set typed attributes.
        REQUIRE(elem1.getTypedAttribute<bool>("customFlag") == false);
        REQUIRE(elem1.getTypedAttribute<Color3>("customColor") == new Color3(0.0f));
        elem1.setTypedAttribute<bool>("customFlag", true);
        elem1.setTypedAttribute<Color3>("customColor", new Color3(1.0f));
        REQUIRE(elem1.getTypedAttribute<bool>("customFlag") == true);
        REQUIRE(elem1.getTypedAttribute<Color3>("customColor") == new Color3(1.0f));
        REQUIRE(elem1.getTypedAttribute<bool>("customColor") == false);
        REQUIRE(elem1.getTypedAttribute<Color3>("customFlag") == new Color3(0.0f));

        // Modify element names.
        elem1.setName("elem1");
        elem2.setName("elem2");
        REQUIRE(elem1.getName() == "elem1");
        REQUIRE(elem2.getName() == "elem2");
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { elem2.setName("elem1"); });

        // Modify element order.
        Document doc2 = doc.copy();
        REQUIRE(doc2.isEqual(doc));
        doc2.setChildIndex("elem1", doc2.getChildIndex("elem2"));
        REQUIRE(!doc2.isEqual(doc));
        doc2.setChildIndex("elem1", doc2.getChildIndex("elem2"));
        REQUIRE(doc2.isEqual(doc));
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { doc2.setChildIndex("elem1", -100); });
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { doc2.setChildIndex("elem1", -1); });
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { doc2.setChildIndex("elem1", 2); });
        REQUIRE_THROWS_AS<MaterialX.Exception>(() => { doc2.setChildIndex("elem1", 100); });
        REQUIRE(doc2.isEqual(doc));

        // Check content document membership.
        Document contentDoc = createDocument();
        contentDoc.setSourceUri("content.mtlx");
        Element contentElem = contentDoc.addChildOfCategory("generic", "contentElem");
        REQUIRE(contentElem.belongsToContentDocument());
        Document lib = createDocument();
        lib.setSourceUri("library.mtlx");
        lib.addChildOfCategory("generic", "libElem");
        contentDoc.importLibrary(lib);
        Element libElem = contentDoc.getChild("libElem");
        REQUIRE(libElem != null);
        REQUIRE(!libElem.belongsToContentDocument());

        // Check content document membership for referenced standard library
        // elements, which remain outside the content document.
        FileSearchPath searchPath = getDefaultDataSearchPath();
        Document stdlib = createDocument();
        loadLibraries(new List<FilePath> { new FilePath("libraries") }, searchPath, stdlib);

        Document docWithDataLibrary = createDocument();
        docWithDataLibrary.setDataLibrary(stdlib);
        Element referencedNodeDef = docWithDataLibrary.getChild("ND_image_color3");
        REQUIRE(referencedNodeDef != null);
        REQUIRE(!referencedNodeDef.belongsToContentDocument());

        Document docWithImportedLibrary = createDocument();
        docWithImportedLibrary.importLibrary(stdlib);
        Element importedNodeDef = docWithImportedLibrary.getChild("ND_image_color3");
        REQUIRE(importedNodeDef != null);
        REQUIRE(!importedNodeDef.belongsToContentDocument());

        // (not in MaterialX) Upstream ends with an orphaned element: a child whose
        // document has been destroyed, for which getDocument throws
        // ExceptionOrphanedElement. An element holds a reference to its parent
        // here, so a document lives for as long as one of its elements does and
        // an element cannot be orphaned in that way.
        {
            Document doc3 = doc.copy();
            Element orphan = doc3.getChild("elem1");
            REQUIRE(orphan != null);
            REQUIRE(orphan.getDocument() == doc3);
        }
    }
}
