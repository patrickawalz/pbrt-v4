// Further reference outputs of the MaterialX C++ library for every document under a directory: reading with comments and newlines,
// writing without XIncludes, tree and graph functions, values, flattening, unit conversion, shader translation, and shader generation
// with other options and with the OSL network generator.
#include <MaterialXCore/Document.h>
#include <MaterialXCore/Unit.h>
#include <MaterialXFormat/XmlIo.h>
#include <MaterialXFormat/Util.h>
#include <MaterialXGenShader/GenContext.h>
#include <MaterialXGenShader/Shader.h>
#include <MaterialXGenShader/Util.h>
#include <MaterialXGenShader/ShaderTranslator.h>
#include <MaterialXGenShader/DefaultColorManagementSystem.h>
#include <MaterialXGenShader/UnitSystem.h>
#include <MaterialXGenOsl/OslShaderGenerator.h>
#include <MaterialXGenOsl/OslNetworkShaderGenerator.h>
#include <filesystem>
#include <fstream>
#include <iostream>
namespace mx = MaterialX;
namespace fs = std::filesystem;

static void setup(mx::GenContext& context, mx::DocumentPtr lib, const mx::FileSearchPath& searchPath)
{
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
}

static void generateAll(const std::string& tag, mx::GenContext& context, mx::DocumentPtr doc, const std::string& base, std::ostream& log)
{
    mx::ShaderGenerator& gen = context.getShaderGenerator();
    gen.registerTypeDefs(doc);
    for (mx::TypedElementPtr element : mx::findRenderableElements(doc))
    {
        std::string name = mx::createValidName(element->getNamePath());
        try
        {
            mx::ShaderPtr shader = gen.generate(name, element, context);
            std::ofstream(base + "." + tag + "." + name + ".osl") << shader->getSourceCode();
            log << tag << " generated " << name << "\n";
        }
        catch (std::exception& e)
        {
            log << tag << " failed " << name << " " << e.what() << "\n";
        }
    }
}

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
        std::ofstream log(base + ".log2");
        mx::FileSearchPath docPath = searchPath;
        docPath.append(mx::FilePath(file).getParentPath());
        try
        {
            // Reading with comments and newlines.
            {
                mx::XmlReadOptions readOptions;
                readOptions.readComments = true;
                readOptions.readNewlines = true;
                mx::DocumentPtr full = mx::createDocument();
                mx::readFromXmlFile(full, file, docPath, &readOptions);
                std::ofstream(base + ".full.xml") << mx::writeToXmlString(full);
            }

            mx::DocumentPtr doc = mx::createDocument();
            mx::readFromXmlFile(doc, file, docPath);
            {
                mx::XmlWriteOptions writeOptions;
                writeOptions.writeXIncludeEnable = false;
                std::ofstream(base + ".noxi.xml") << mx::writeToXmlString(doc, &writeOptions);
            }
            doc->setDataLibrary(lib);
            std::ofstream(base + ".pretty") << mx::prettyPrint(doc);

            // Tree and graph functions.
            std::pair<int, int> version = doc->getVersionIntegers();
            log << "version " << version.first << " " << version.second << "\n";
            for (const std::string& uri : doc->getReferencedSourceUris())
                log << "uri " << mx::FilePath(uri).getBaseName() << "\n";
            for (mx::OutputPtr output : doc->getMaterialOutputs())
                log << "materialoutput " << output->getNamePath() << "\n";
            {
                mx::DocumentPtr copy = doc->copy();
                mx::ElementEquivalenceOptions options;
                std::string message;
                log << "equivalent " << doc->isEquivalent(copy, options, &message) << " equal " << (*doc == *copy) << "\n" << message;
            }
            for (mx::ElementPtr elem : doc->traverseTree())
            {
                mx::ValueElementPtr valueElem = elem->asA<mx::ValueElement>();
                if (valueElem)
                {
                    mx::ValuePtr value = valueElem->getValue();
                    mx::ValuePtr defaultValue = valueElem->getDefaultValue();
                    log << "value " << elem->getNamePath() << " [" << (value ? value->getTypeString() : "-") << "] [" << (value ? value->getValueString() : "-")
                        << "] [" << valueElem->getResolvedValueString() << "] [" << (defaultValue ? defaultValue->getValueString() : "-") << "] ["
                        << valueElem->getActiveUnit() << "] [" << elem->getActiveColorSpace() << "]\n";
                }
                mx::NodePtr node = elem->asA<mx::Node>();
                if (node)
                {
                    log << "node " << node->getNamePath() << " downstream";
                    for (mx::PortElementPtr port : node->getDownstreamPorts())
                        log << " " << port->getNamePath();
                    log << " outputs";
                    for (mx::OutputPtr output : mx::getConnectedOutputs(node))
                        log << " " << output->getNamePath();
                    log << "\n";
                    if (node->getType() == mx::MATERIAL_TYPE_STRING)
                    {
                        log << "shadernodes " << node->getNamePath();
                        for (mx::NodePtr shaderNode : mx::getShaderNodes(node))
                            log << " " << shaderNode->getNamePath();
                        log << " |";
                        for (mx::NodePtr shaderNode : mx::getShaderNodes(node, mx::SURFACE_SHADER_TYPE_STRING, "genosl"))
                            log << " " << shaderNode->getNamePath();
                        log << " | bindings";
                        for (mx::MaterialAssignPtr assign : mx::getGeometryBindings(node))
                            log << " " << assign->getNamePath();
                        log << "\n";
                    }
                }
                mx::GraphElementPtr graph = elem->asA<mx::GraphElement>();
                if (graph)
                {
                    log << "topo " << (graph->getNamePath().empty() ? "<document>" : graph->getNamePath());
                    for (mx::ElementPtr sorted : graph->topologicalSort())
                        log << " " << sorted->getName();
                    log << "\n";
                    std::string dotName = graph->getNamePath().empty() ? "document" : mx::createValidName(graph->getNamePath());
                    std::ofstream(base + ".dot." + dotName) << graph->asStringDot();
                }
                mx::OutputPtr output = elem->asA<mx::Output>();
                if (output)
                {
                    log << "output " << output->getNamePath() << " cycle " << output->hasUpstreamCycle() << " edges";
                    for (mx::Edge edge : output->traverseGraph())
                        log << " " << edge.getUpstreamElement()->getName();
                    mx::NodePtr world = mx::connectsToWorldSpaceNode(output);
                    log << " world " << (world ? world->getName() : "-") << "\n";
                }
            }

            // Flattening.
            try
            {
                mx::DocumentPtr flat = doc->copy();
                flat->flattenSubgraphs();
                for (mx::NodeGraphPtr graph : flat->getNodeGraphs())
                {
                    if (graph->getActiveSourceUri() == flat->getSourceUri())
                        graph->flattenSubgraphs();
                }
                std::ofstream(base + ".flat.xml") << mx::writeToXmlString(flat);
            }
            catch (std::exception& e)
            {
                log << "flatten error " << e.what() << "\n";
            }

            // Unit conversion.
            try
            {
                mx::DocumentPtr converted = doc->copy();
                mx::UnitConverterRegistryPtr registry = mx::UnitConverterRegistry::create();
                mx::UnitTypeDefPtr distance = lib->getUnitTypeDef("distance");
                registry->addUnitConverter(distance, mx::LinearUnitConverter::create(distance));
                log << "convert " << registry->convertToUnit(converted, "distance", "meter") << "\n";
                std::ofstream(base + ".units.xml") << mx::writeToXmlString(converted);
            }
            catch (std::exception& e)
            {
                log << "convert error " << e.what() << "\n";
            }

            // Shader translation.
            for (const char* dest : { "gltf_pbr", "UsdPreviewSurface", "open_pbr_surface", "standard_surface" })
            {
                try
                {
                    mx::DocumentPtr translated = doc->copy();
                    mx::ShaderTranslator::create()->translateAllMaterials(translated, dest);
                    std::ofstream(base + ".to_" + dest + ".xml") << mx::writeToXmlString(translated);
                    log << "translate " << dest << " ok\n";
                }
                catch (std::exception& e)
                {
                    log << "translate " << dest << " error " << e.what() << "\n";
                }
            }

            // Transparency and shading queries.
            for (mx::TypedElementPtr element : mx::findRenderableElements(doc))
            {
                log << "query " << element->getNamePath() << " transparent " << mx::isTransparentSurface(element, "genosl")
                    << " shading " << mx::elementRequiresShading(element) << "\n";
            }
            for (mx::TypedElementPtr element : mx::findRenderableMaterialNodes(doc))
                log << "materialnode " << element->getNamePath() << "\n";

            // Generation with other options.
            {
                mx::GenContext context(mx::OslShaderGenerator::create());
                setup(context, lib, searchPath);
                context.getOptions().targetDistanceUnit = "centimeter";
                context.getOptions().targetColorSpaceOverride = "lin_rec709";
                context.getOptions().shaderInterfaceType = mx::SHADER_INTERFACE_REDUCED;
                context.getOptions().fileTextureVerticalFlip = true;
                context.getOptions().elideConstantNodes = false;
                generateAll("reduced", context, doc, base, log);
            }
            {
                mx::GenContext context(mx::OslShaderGenerator::create());
                setup(context, lib, searchPath);
                context.getOptions().targetDistanceUnit = "meter";
                context.getOptions().emitColorTransforms = false;
                context.getOptions().premultipliedBsdfAdd = true;
                context.getOptions().distributeLayerOverBsdfMix = true;
                context.getOptions().oslImplicitSurfaceShaderConversion = false;
                context.getOptions().addUpstreamDependencies = false;
                generateAll("refactor", context, doc, base, log);
            }
            {
                mx::GenContext context(mx::OslNetworkShaderGenerator::create());
                setup(context, lib, searchPath);
                context.getOptions().targetDistanceUnit = "meter";
                context.getOptions().oslConnectCiWrapper = true;
                generateAll("network", context, doc, base, log);
            }
        }
        catch (std::exception& e)
        {
            log << "error " << e.what() << "\n";
        }
    }
    return 0;
}
