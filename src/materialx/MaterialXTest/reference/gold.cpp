// Writes reference outputs of the MaterialX C++ library for every document under a directory: the document written back as XML, its
// validation result, the node definition and implementation of every node, and the OSL source of every renderable element.
#include <MaterialXCore/Document.h>
#include <MaterialXCore/Unit.h>
#include <MaterialXFormat/XmlIo.h>
#include <MaterialXFormat/Util.h>
#include <MaterialXGenShader/GenContext.h>
#include <MaterialXGenShader/Shader.h>
#include <MaterialXGenShader/Util.h>
#include <MaterialXGenShader/DefaultColorManagementSystem.h>
#include <MaterialXGenShader/UnitSystem.h>
#include <MaterialXGenOsl/OslShaderGenerator.h>
#include <filesystem>
#include <fstream>
#include <iostream>
namespace mx = MaterialX;
namespace fs = std::filesystem;

int main(int argc, char** argv)
{
    std::string root = argv[1], docs = argv[2], out = argv[3];
    mx::FileSearchPath searchPath(root);
    mx::DocumentPtr lib = mx::createDocument();
    mx::loadLibraries({ "libraries" }, searchPath, lib);

    std::vector<std::string> files;
    for (auto& e : fs::recursive_directory_iterator(docs))
        if (e.path().extension() == ".mtlx") files.push_back(e.path().string());
    std::sort(files.begin(), files.end());

    for (const std::string& file : files)
    {
        std::string rel = fs::relative(file, docs).string();
        std::string base = out + "/" + rel;
        fs::create_directories(fs::path(base).parent_path());
        std::ofstream log(base + ".log");
        try
        {
            mx::DocumentPtr doc = mx::createDocument();
            mx::FileSearchPath docPath = searchPath;
            docPath.append(mx::FilePath(file).getParentPath());
            mx::readFromXmlFile(doc, file, docPath);
            std::ofstream(base + ".xml") << mx::writeToXmlString(doc);
            doc->setDataLibrary(lib);
            std::string message;
            bool valid = doc->validate(&message);
            log << "valid " << valid << "\n" << message << "\n";
            for (mx::ElementPtr elem : doc->traverseTree())
            {
                mx::NodePtr node = elem->asA<mx::Node>();
                if (!node) continue;
                mx::NodeDefPtr nd = node->getNodeDef();
                mx::InterfaceElementPtr impl = nd ? nd->getImplementation("genosl") : nullptr;
                log << "node " << node->getNamePath() << " " << (nd ? nd->getName() : "-") << " " << (impl ? impl->getName() : "-") << "\n";
            }
            mx::GenContext context(mx::OslShaderGenerator::create());
            context.registerSourceCodeSearchPath(searchPath);
            mx::ShaderGenerator& gen = context.getShaderGenerator();
            mx::DefaultColorManagementSystemPtr cms = mx::DefaultColorManagementSystem::create(gen.getTarget());
            cms->loadLibrary(lib);
            gen.setColorManagementSystem(cms);
            mx::UnitSystemPtr units = mx::UnitSystem::create(gen.getTarget());
            units->loadLibrary(lib);
            units->setUnitConverterRegistry(mx::UnitConverterRegistry::create());
            mx::UnitTypeDefPtr distance = lib->getUnitTypeDef("distance");
            units->getUnitConverterRegistry()->addUnitConverter(distance, mx::LinearUnitConverter::create(distance));
            mx::UnitTypeDefPtr angle = lib->getUnitTypeDef("angle");
            units->getUnitConverterRegistry()->addUnitConverter(angle, mx::LinearUnitConverter::create(angle));
            gen.setUnitSystem(units);
            context.getOptions().targetDistanceUnit = "meter";
            context.getOptions().targetColorSpaceOverride = "lin_rec709";
            gen.registerTypeDefs(doc);
            std::vector<mx::TypedElementPtr> elements = mx::findRenderableElements(doc);
            for (mx::TypedElementPtr element : elements)
            {
                std::string name = mx::createValidName(element->getNamePath());
                log << "renderable " << element->getNamePath() << " " << name << "\n";
                try
                {
                    mx::ShaderPtr shader = gen.generate(name, element, context);
                    std::ofstream(base + "." + name + ".osl") << shader->getSourceCode();
                    log << "generated " << name << "\n";
                }
                catch (std::exception& e)
                {
                    log << "failed " << name << " " << e.what() << "\n";
                }
            }
        }
        catch (std::exception& e)
        {
            log << "error " << e.what() << "\n";
        }
    }
    return 0;
}
