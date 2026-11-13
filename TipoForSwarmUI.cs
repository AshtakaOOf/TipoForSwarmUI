using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.Linq;

namespace SwarmExtensions.TIPO
{
    public class TipoForSwarmUI : Extension
    {
        public TipoForSwarmUI()
        {
            ExtensionAuthor = "Hugs288";
            License = "MIT";
            Description = "Adds a parameter group for TIPO (anime tag prompt gen).";
        }

        public static T2IRegisteredParam<string> BanTags, TipoModel, Format, TagLength, NlLength, PromptType, Device;
        public static T2IRegisteredParam<bool> NoFormatting;
        public static T2IRegisteredParam<double> Temperature, TopP, MinP;
        public static T2IRegisteredParam<int> TopK;
        public static T2IRegisteredParam<long> TipoSeed;

        public static T2IParamGroup TIPOParamGroup;

        public static List<string> DynamicTipoModelList = ["(Requires ComfyUI Backend Connection)"];
        public static List<string> DynamicTipoDeviceList = ["cuda", "cpu"];
        private static readonly object ModelListLock = new();

        public static bool HandleTipoMetadata(T2IParamInput user_input, string key, string value)
        {
            if (key != "tipo_prompt")
            {
                return false;
            }
            string originalPrompt = user_input.Get(T2IParamTypes.Prompt);
            if (!user_input.ExtraMeta.ContainsKey("original_prompt") && !string.IsNullOrEmpty(originalPrompt))
            {
                user_input.ExtraMeta["original_prompt"] = originalPrompt;
            }
            user_input.Set(T2IParamTypes.Prompt, value);
            return true;
        }

        static List<string> ComboValues(JObject info, string input)
        {
            if (info?["TIPO"] is null)
            {
                return null;
            }
            JToken list = null;
            if (!ComfyUIBackendExtension.TryGetRequiredInputs(info, "TIPO", input, out list))
            {
                if (info["TIPO"]?["input"]?["optional"]?[input] is not JArray arr)
                {
                    return null;
                }
                list = arr[0] as JArray ?? (arr.ElementAtOrDefault(1) as JObject)?["options"];
            }
            if (list is not JArray values)
            {
                return null;
            }
            return [.. values.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s))];
        }

        static bool TryApplyTipoPrompt(JObject workflow, JArray finalPrompt, JArray tipoOutput, out JArray applied)
        {
            applied = finalPrompt;
            string id = finalPrompt?[0]?.ToString() ?? "6";
            if (!workflow.TryGetValue(id, out JToken token) || token is not JObject node || node["inputs"] is not JObject inputs)
            {
                return false;
            }
            if (node["class_type"]?.ToString() == "ConditioningMultiply" && inputs["conditioning"] is JArray inner)
            {
                return TryApplyTipoPrompt(workflow, inner, tipoOutput, out applied);
            }
            if (inputs.ContainsKey("text_g"))
            {
                inputs["text_g"] = tipoOutput;
                if (inputs.ContainsKey("text_l"))
                {
                    inputs["text_l"] = tipoOutput;
                }
            }
            else
            {
                string field = new[] { "prompt", "text", "lyrics", "caption" }.FirstOrDefault(inputs.ContainsKey);
                if (field is null)
                {
                    return false;
                }
                inputs[field] = tipoOutput;
            }
            applied = [id, finalPrompt?[1] ?? 0];
            return true;
        }

        public override void OnInit()
        {
            InstallableFeatures.RegisterInstallableFeature(new("TIPO", "tipo_prompt_generation", "https://github.com/KohakuBlueleaf/z-tipo-extension", "KohakuBlueleaf", "This will install TIPO developed by KohakuBlueleaf.\nDo you wish to install?"));
            ScriptFiles.Add("assets/TIPO.js");
            ComfyUIBackendExtension.NodeToFeatureMap["TIPO"] = "tipo_prompt_generation";

            TIPOParamGroup = new("TIPO Prompt Generation", Toggles: true, Open: false, IsAdvanced: false, OrderPriority: 50, Description: "Use TIPO to upsample prompt.");

            PromptType = T2IParamTypes.Register<string>(new(Name: "[TIPO] Prompt Type", Description: "Treat main prompt as 'tags' or 'natural language'.", Default: "tags", GetValues: (_) => ["tags", "natural language"], Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 2));
            BanTags = T2IParamTypes.Register<string>(new(Name: "[TIPO] Banned Tags", Description: "Comma-separated list of tags to ban. Regex is supported.", Default: "", Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 3, ViewType: ParamViewType.PROMPT));
            TipoModel = T2IParamTypes.Register<string>(new(Name: "[TIPO] TIPO Model", Description: "TIPO model to use. 500m-ft is recommended.", Default: "", GetValues: (_) => { lock (ModelListLock) { return [.. DynamicTipoModelList.AsEnumerable().Reverse()]; } }, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 4));
            Format = T2IParamTypes.Register<string>(new(Name: "[TIPO] Format", Description: "TIPO output format string. Extended is natural language.", Default: "<|special|>,\n<|characters|>, <|copyrights|>,\n<|artist|>,\n\n<|general|>,\n\n<|extended|>.\n\n<|quality|>, <|meta|>, <|rating|>", Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 5, ViewType: ParamViewType.PROMPT, Toggleable: true));
            NoFormatting = T2IParamTypes.Register<bool>(new(Name: "[TIPO] No Formatting", Description: "Use unformatted TIPO output.", Default: "false", IgnoreIf: "false", Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 5.5));
            Temperature = T2IParamTypes.Register<double>(new(Name: "[TIPO] Temperature", Description: "TIPO sampling temperature. Higher = more random outputs", Default: "0.5", Min: 0, Max: 2, Step: 0.01, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 6, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            TopP = T2IParamTypes.Register<double>(new(Name: "[TIPO] Top P", Description: "TIPO sampling Top P.", Default: "0.95", Min: 0, Max: 1, Step: 0.01, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 7, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            MinP = T2IParamTypes.Register<double>(new(Name: "[TIPO] Min P", Description: "TIPO sampling Min P.", Default: "0.05", Min: 0, Max: 1, Step: 0.01, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 8, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            TopK = T2IParamTypes.Register<int>(new(Name: "[TIPO] Top K", Description: "TIPO sampling Top K.", Default: "80", Min: 0, Max: 200, Step: 1, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 9, ViewType: ParamViewType.SLIDER, IsAdvanced: true));
            TagLength = T2IParamTypes.Register<string>(new(Name: "[TIPO] Tag Length", Description: "Target tag length.", Default: "long", GetValues: (_) => ["very_short", "short", "long", "very_long"], Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 10));
            NlLength = T2IParamTypes.Register<string>(new(Name: "[TIPO] NL Length", Description: "Target natural language length.", Default: "long", GetValues: (_) => ["very_short", "short", "long", "very_long"], Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 11));
            TipoSeed = T2IParamTypes.Register<long>(new(Name: "[TIPO] Seed", Description: "TIPO generation seed. Use -1 for random, uses the image generation seed if disabled.", Default: "-1", Min: -1, Max: long.MaxValue, Step: 1, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 12, ViewType: ParamViewType.SEED, Clean: T2IParamTypes.Seed.Type.Clean, Toggleable: true));
            Device = T2IParamTypes.Register<string>(new(Name: "[TIPO] Device", Description: "Device override for TIPO.\nDefault is cpu because cuda has reproducibility issues.", Default: "cpu", GetValues: (_) => { lock (ModelListLock) { return [.. DynamicTipoDeviceList]; } }, Group: TIPOParamGroup, FeatureFlag: "tipo_prompt_generation", OrderPriority: 13, IsAdvanced: true));

            ComfyUIBackendExtension.RawObjectInfoParsers.Add(info =>
            {
                lock (ModelListLock)
                {
                    if (ComboValues(info, "tipo_model") is List<string> models && !models.SequenceEqual(DynamicTipoModelList))
                    {
                        DynamicTipoModelList = models.Count > 0 ? models : ["(No models found)"];
                    }
                    if (ComboValues(info, "device") is List<string> devices && devices.Count > 0 && !devices.SequenceEqual(DynamicTipoDeviceList))
                    {
                        DynamicTipoDeviceList = devices;
                    }
                }
            });

            ComfyUIAPIAbstractBackend.AltCustomMetadataHandlers.Add(HandleTipoMetadata);

            // after positive prompt encode, before later conditioning wraps
            WorkflowGenerator.AddStep(g =>
            {
                bool isTipoGroupActive = T2IParamTypes.Types.Values
                    .Where(p => p.Group == TIPOParamGroup && p.ID != null)
                    .Any(p => g.UserInput.TryGetRaw(p, out _));
                if (!isTipoGroupActive)
                {
                    return;
                }
                if (!g.Features.Contains("tipo_prompt_generation"))
                {
                    throw new SwarmUserErrorException("TIPO parameters were provided, but the backend does not have the TIPO custom node installed or recognized.");
                }

                string mainPromptText = g.UserInput.Get(T2IParamTypes.Prompt) ?? "";
                string promptTypeText = g.UserInput.Get(PromptType, "tags");
                bool useUnformatted = g.UserInput.Get(NoFormatting);
                string deviceSelection = g.UserInput.Get(Device);
                string tipoModel = g.UserInput.Get(TipoModel);
                if (string.IsNullOrEmpty(tipoModel) || tipoModel.StartsWith('('))
                {
                    lock (ModelListLock)
                    {
                        tipoModel = DynamicTipoModelList.FirstOrDefault(m => !m.StartsWith('(')) ?? tipoModel;
                    }
                }

                long tipoSeedRequest = g.UserInput.Get(TipoSeed);
                long finalTipoSeed;
                if (g.UserInput.TryGet(TipoSeed, out _))
                {
                    finalTipoSeed = tipoSeedRequest == -1 ? Random.Shared.Next() : tipoSeedRequest;
                }
                else
                {
                    finalTipoSeed = g.UserInput.Get(T2IParamTypes.Seed);
                }
                g.UserInput.Set(TipoSeed, finalTipoSeed);

                JObject tipoInputs = new()
                {
                    ["tags"] = (promptTypeText == "tags") ? mainPromptText : "",
                    ["nl_prompt"] = (promptTypeText == "natural language") ? mainPromptText : "",
                    ["ban_tags"] = g.UserInput.Get(BanTags) ?? "",
                    ["tipo_model"] = tipoModel,
                    ["format"] = g.UserInput.Get(Format),
                    ["width"] = g.UserInput.Get(T2IParamTypes.Width),
                    ["height"] = g.UserInput.Get(T2IParamTypes.Height),
                    ["temperature"] = g.UserInput.Get(Temperature),
                    ["top_p"] = g.UserInput.Get(TopP),
                    ["min_p"] = g.UserInput.Get(MinP),
                    ["top_k"] = g.UserInput.Get(TopK),
                    ["tag_length"] = g.UserInput.Get(TagLength),
                    ["nl_length"] = g.UserInput.Get(NlLength),
                    ["seed"] = finalTipoSeed,
                    ["device"] = deviceSelection
                };
                string tipoNodeId = g.CreateNode("TIPO", tipoInputs, g.GetStableDynamicID(100, 0));
                JArray tipoOutputLink = [tipoNodeId, useUnformatted ? 2 : 0]; // 0 = formatted, 2 = unformatted

                g.CreateNode("SwarmAddSaveMetadataWS", new JObject()
                {
                    ["key"] = "tipo_prompt",
                    ["value"] = tipoOutputLink
                }, g.GetStableDynamicID(100, 1));

                if (!TryApplyTipoPrompt(g.Workflow, g.FinalPrompt, tipoOutputLink, out JArray applied))
                {
                    throw new SwarmUserErrorException("TIPO ran, but the text encoder prompt input could not be found to attach its output.");
                }
                g.FinalPrompt = applied;
            }, -7.9);
        }
    }
}
