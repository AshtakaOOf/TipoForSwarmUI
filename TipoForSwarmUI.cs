using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SwarmExtensions.TIPO
{
    public class TipoForSwarmUI : Extension
    {
        public TipoForSwarmUI()
        {
            ExtensionName = "TipoForSwarmUI";
        }

        // Parameter Definitions
        public static T2IRegisteredParam<string> BanTags, TipoModel, Format, TagLength, NlLength, PromptType, Device;
        public static T2IRegisteredParam<bool> NoFormatting;
        public static T2IRegisteredParam<double> Temperature, TopP, MinP;
        public static T2IRegisteredParam<int> TopK;
        public static T2IRegisteredParam<long> TipoSeed;

        // Parameter Group
        public static T2IParamGroup TIPOParamGroup;

        // Static list for dynamic model population by the backend parser
        public static List<string> DynamicTipoModelList = ["(Requires ComfyUI Backend Connection)"];
        private static readonly object ModelListLock = new(); // Lock for thread safety

        public static bool HandleTipoMetadata(T2IParamInput user_input, string key, string value)
        {
            if (key == "tipo_prompt")
            {
                string originalPrompt = user_input.Get(T2IParamTypes.Prompt);

                // Store the original prompt in the ExtraMeta dictionary, or use the prompt before wildcard processing if it exists
                if (!user_input.ExtraMeta.ContainsKey("original_prompt") && !string.IsNullOrEmpty(originalPrompt))
                {
                    user_input.ExtraMeta["original_prompt"] = originalPrompt;
                }
                // Replace the original prompt in the user input data with the TIPO-generated value.
                user_input.Set(T2IParamTypes.Prompt, value);

                // Indicate that we've handled this key, so Swarm doesn't also add it as 'custom_tipo_prompt' to ExtraMeta
                return true;
            }
            // If it's not our key, let other handlers or the default logic process it
            return false;
        }

        public override void OnInit()
        {
            // Install
            InstallableFeatures.RegisterInstallableFeature(new("TIPO", "tipo_prompt_generation", "https://github.com/KohakuBlueleaf/z-tipo-extension", "KohakuBlueleaf", "This will install TIPO developed by KohakuBlueleaf.\nDo you wish to install?"));
            ScriptFiles.Add("assets/TIPO.js");
            // Map ComfyUI Node name to SwarmUI Feature ID for backend capability detection
            ComfyUIBackendExtension.NodeToFeatureMap["TIPO"] = "tipo_prompt_generation";

            // Define the Parameter Group
            TIPOParamGroup = new("TIPO Prompt Generation", Toggles: true, Open: false, IsAdvanced: false, OrderPriority: 50, Description: "Use TIPO to upsample prompt.");

            // Register Parameters
            PromptType = T2IParamTypes.Register<string>(new(Name: "[TIPO] Prompt Type", Description: "Treat main prompt as 'tags' or 'natural language'.", Default: "tags", GetValues: (_) => ["tags", "natural language"], Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 2));
            BanTags = T2IParamTypes.Register<string>(new(Name: "[TIPO] Banned Tags", Description: "Comma-separated list of tags to ban.", Default: "", Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 3, ViewType: ParamViewType.PROMPT));
            TipoModel = T2IParamTypes.Register<string>(new(Name: "[TIPO] TIPO Model", Description: "TIPO model to use. 500m-ft is recommended.", Default: "", GetValues: (_) => { lock (ModelListLock) { return DynamicTipoModelList.AsEnumerable().Reverse().ToList(); } }, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 4));
            Format = T2IParamTypes.Register<string>(new(Name: "[TIPO] Format", Description: "TIPO output format string. Extended is natural language.", Default: "<|special|>,\n<|characters|>, <|copyrights|>,\n<|artist|>,\n\n<|general|>,\n\n<|extended|>.\n\n<|quality|>, <|meta|>, <|rating|>", Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 5, ViewType: ParamViewType.PROMPT, Toggleable: true));
            NoFormatting = T2IParamTypes.Register<bool>(new(Name: "[TIPO] No Formatting", Description: "Use unformatted TIPO output.", Default: "false", IgnoreIf: "false", Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 5.5));
            Temperature = T2IParamTypes.Register<double>(new(Name: "[TIPO] Temperature", Description: "TIPO sampling temperature. Higher = more random outputs", Default: "0.5", Min: 0, Max: 2, Step: 0.01, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 6, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            TopP = T2IParamTypes.Register<double>(new(Name: "[TIPO] Top P", Description: "TIPO sampling Top P.", Default: "0.95", Min: 0, Max: 1, Step: 0.01, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 7, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            MinP = T2IParamTypes.Register<double>(new(Name: "[TIPO] Min P", Description: "TIPO sampling Min P.", Default: "0.05", Min: 0, Max: 1, Step: 0.01, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 8, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            TopK = T2IParamTypes.Register<int>(new(Name: "[TIPO] Top K", Description: "TIPO sampling Top K.", Default: "80", Min: 0, Max: 200, Step: 1, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 9, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            TagLength = T2IParamTypes.Register<string>(new(Name: "[TIPO] Tag Length", Description: "Target tag length.", Default: "long", GetValues: (_) => ["very_short", "short", "long", "very_long"], Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 10));
            NlLength = T2IParamTypes.Register<string>(new(Name: "[TIPO] NL Length", Description: "Target natural language length.", Default: "long", GetValues: (_) => ["very_short", "short", "long", "very_long"], Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 11));
            TipoSeed = T2IParamTypes.Register<long>(new(Name: "[TIPO] Seed", Description: "TIPO generation seed. Use -1 for random, uses the image generation seed if disabled.", Default: "-1", Min: -1, Max: long.MaxValue, Step: 1, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 12, ViewType: ParamViewType.SEED, Clean: T2IParamTypes.Seed.Type.Clean, Toggleable: true));
            Device = T2IParamTypes.Register<string>(new(Name: "[TIPO] Device", Description: "Device override for TIPO. \nDefault is cpu because cuda has reproducability issues.", Default: "cpu", GetValues: (_) => ["cuda", "cpu"], Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 13, IsAdvanced: true));

            // Add Parser for Dynamic Model List from ComfyUI Backend Info
            ComfyUIBackendExtension.RawObjectInfoParsers.Add(rawObjectInfo =>
            {
                if (rawObjectInfo?["TIPO"]?["input"]?["required"]?["tipo_model"] is JArray modelListToken
                    && modelListToken.Count > 0 && modelListToken[0] is JArray actualList)
                {
                    lock (ModelListLock)
                    {
                        var newModelList = actualList.Select(t => t.ToString()).OrderBy(m => m).ToList();
                        // Only update if the list content has actually changed
                        if (!newModelList.SequenceEqual(DynamicTipoModelList))
                        {
                            DynamicTipoModelList = newModelList.Any() ? newModelList : ["(No TIPO models found on backend)"];
                        }
                    }
                }
            });

            ComfyUIAPIAbstractBackend.AltCustomMetadataHandlers.Add(HandleTipoMetadata);

            WorkflowGenerator.AddStep(g =>
            {
                // Check if any TIPO parameter is actively used in the input
                 bool isTipoGroupActive = T2IParamTypes.Types.Values
                    .Where(p => p.Group == TIPOParamGroup && p.ID != null) // Ensure p.ID is not null before checking
                    .Any(p => g.UserInput.TryGetRaw(p, out _));

                if (isTipoGroupActive)
                {
                    if (!g.Features.Contains("tipo_prompt_generation"))
                    {
                        throw new SwarmUserErrorException("TIPO parameters were provided, but the backend does not have the TIPO custom node installed or recognized.");
                    }

                    string mainPromptText = g.UserInput.Get(T2IParamTypes.Prompt) ?? "";
                    string promptTypeText = g.UserInput.Get(PromptType, "tags");
                    bool useUnformatted = g.UserInput.Get(NoFormatting);
                    string deviceSelection = g.UserInput.Get(Device);
                    // Get requested TIPO seed and determine final seed value
                    long tipoSeedRequest = g.UserInput.Get(TipoSeed);
                    bool useFixedSeedControl = true;
                    long finalTipoSeed;

                    if (g.UserInput.TryGet(TipoSeed, out _))
                    {
                        if (tipoSeedRequest == -1) // User explicitly set TIPO seed to -1 (Random)
                        {
                            useFixedSeedControl = false;
                            finalTipoSeed = Random.Shared.Next(); // Generate a new random seed specifically for TIPO
                        }
                        else // User explicitly set a specific TIPO seed
                        {
                            finalTipoSeed = tipoSeedRequest;
                        }
                    }
                    else // TIPO Seed parameter is disabled (untoggled in UI)
                    {
                        // Use the main image generation seed
                        finalTipoSeed = g.UserInput.Get(T2IParamTypes.Seed);
                    }

                    g.UserInput.Set(TipoSeed, finalTipoSeed); // Set the calculated seed back into UserInput for metadata

                    JObject tipoInputs = new()
                    {
                        ["tags"] = (promptTypeText == "tags") ? mainPromptText : "",
                        ["nl_prompt"] = (promptTypeText == "natural language") ? mainPromptText : "",
                        ["ban_tags"] = g.UserInput.Get(BanTags),
                        ["tipo_model"] = g.UserInput.Get(TipoModel),
                        ["format"] = g.UserInput.Get(Format),
                        ["width"] = g.UserInput.Get(T2IParamTypes.Width),
                        ["height"] = g.UserInput.Get(T2IParamTypes.Height),
                        ["temperature"] = g.UserInput.Get(Temperature),
                        ["top_p"] = g.UserInput.Get(TopP),
                        ["min_p"] = g.UserInput.Get(MinP),
                        ["top_k"] = g.UserInput.Get(TopK),
                        ["tag_length"] = g.UserInput.Get(TagLength),
                        ["nl_length"] = g.UserInput.Get(NlLength),
                        ["seed"] = finalTipoSeed, // Use the final calculated seed for the node input
                        ["device"] = deviceSelection
                    };
                    if (useFixedSeedControl)
                    {
                        tipoInputs["control_after_generate"] = "fixed";
                    }
                    string tipoNodeId = g.CreateNode("TIPO", tipoInputs, g.GetStableDynamicID(100, 0));
                    int tipoOutputIndex = useUnformatted ? 2 : 0; // 0 = formatted, 2 = unformatted
                    JArray tipoOutputLink = new JArray { tipoNodeId, tipoOutputIndex };

                    // Add SwarmAddSaveMetadataWS node to save the TIPO output
                    JObject metaDataInputs = new()
                    {
                        ["key"] = "tipo_prompt",
                        ["value"] = tipoOutputLink
                    };

                    g.CreateNode("SwarmAddSaveMetadataWS", metaDataInputs, g.GetStableDynamicID(100, 1));

                    string targetEncoderId = g.FinalPrompt?[0]?.ToString() ?? "6"; // Try state, fallback default

                    if (g.Workflow.TryGetValue(targetEncoderId, out JToken targetEncoderToken)
                        && targetEncoderToken is JObject targetEncoderNode
                        && targetEncoderNode["inputs"] is JObject encoderInputs)
                    {
                        if (targetEncoderNode["class_type"]?.ToString().Contains("CLIPTextEncode") ?? false)
                        {
                            string inputName = encoderInputs.ContainsKey("text_g") ? "text_g" : "text";
                            if (encoderInputs.ContainsKey(inputName))
                            {
                                if (inputName == "text_g" && encoderInputs.ContainsKey("text_l")) { encoderInputs["text_l"] = tipoOutputLink; }
                                encoderInputs[inputName] = tipoOutputLink;
                                g.FinalPrompt = new JArray { targetEncoderId, g.FinalPrompt?[1] ?? 0 };
                            }
                        }
                    }
                }
            }, -0.1); // Run after core nodes are likely created
        }
    }
}
