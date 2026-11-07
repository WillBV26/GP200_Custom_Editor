using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gp200.Replica;

internal enum AiProvider { Gemini, Anthropic, OpenRouter, LocalAi }
internal enum AiPresetMode { NewSong, Refine, OptimizeForLive }
internal enum AiPerformanceMode { Patch, Stomp, User, Scenes }
internal enum AiChainTopology { Auto, Series, DualChain }
internal sealed record AiSource(string Title,string Url);

internal sealed record AiGeneratedPreset(string Name, PresetDocument Document,ScenePresetBundle? SceneBundle=null,IReadOnlyList<AiSource>? Sources=null,string? Section=null,PresetDocument? RefinedFrom=null);

internal static class AiApiKeyResolver
{
    static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Gp200Replica",
        "ai_config.json");

    internal static string? ReadSavedKey(AiProvider provider, string? configPath = null)
    {
        if (provider == AiProvider.LocalAi) return "local";
        string path = configPath ?? DefaultConfigPath;
        var saved = ReadConfig(path);
        string property = KeyProperty(provider);
        if (!saved.TryGetValue(property, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            if (!saved.TryGetValue("api_key", out value) || string.IsNullOrWhiteSpace(value))
                return null;
        }

        if (!value.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            SaveKey(provider, value, true, path);
            return value;
        }

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(value["dpapi:".Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null;
        }
    }

    internal static void SaveKey(AiProvider provider, string key, bool remember, string? configPath = null)
    {
        if (provider == AiProvider.LocalAi) return;
        string path = configPath ?? DefaultConfigPath;
        var saved = ReadConfig(path);
        string property = KeyProperty(provider);
        if (remember && !string.IsNullOrWhiteSpace(key))
        {
            byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
            saved[property] = "dpapi:" + Convert.ToBase64String(protectedBytes);
        }
        else
            saved.Remove(property);

        saved.Remove("api_key");
        saved["remember"] = saved.Keys.Any(name => name.EndsWith("_key", StringComparison.Ordinal)) ? "true" : "false";
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The AI key configuration path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static bool IsRememberEnabled(string? configPath = null)
    {
        var saved = ReadConfig(configPath ?? DefaultConfigPath);
        return saved.Keys.Any(name => name.EndsWith("_key", StringComparison.Ordinal));
    }

    internal static string? ResolveKey(AiProvider provider,string? explicitKey=null)
    {
        if (provider == AiProvider.LocalAi) return "local";
        if(!string.IsNullOrWhiteSpace(explicitKey))
            return explicitKey.Trim();

        string? saved = ReadSavedKey(provider);
        if (!string.IsNullOrWhiteSpace(saved))
            return saved.Trim();

        string? key=Environment.GetEnvironmentVariable("GP200_AI_API_KEY");
        if(!string.IsNullOrWhiteSpace(key))return key.Trim();

        string envName=provider switch
        {
            AiProvider.Gemini=>"GEMINI_API_KEY",
            AiProvider.Anthropic=>"ANTHROPIC_API_KEY",
            AiProvider.OpenRouter=>"OPENROUTER_API_KEY",
            AiProvider.LocalAi=>"LOCAL_OLLAMA_HOST",
            _=>""
        };
        if(!string.IsNullOrEmpty(envName))
        {
            key=Environment.GetEnvironmentVariable(envName);
            if(!string.IsNullOrWhiteSpace(key))return key.Trim();
        }
        return null;
    }

    internal static string? GetDetectedEnvVarName(AiProvider provider,string? explicitKey=null)
    {
        if (provider == AiProvider.LocalAi) return "Localhost (Ollama / LM Studio)";
        if(!string.IsNullOrWhiteSpace(explicitKey))return null;
        if (!string.IsNullOrWhiteSpace(ReadSavedKey(provider))) return "ai_config.json (Almacenamiento Local)";
        if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GP200_AI_API_KEY")))return "GP200_AI_API_KEY";
        string env=provider switch
        {
            AiProvider.Gemini=>"GEMINI_API_KEY",
            AiProvider.Anthropic=>"ANTHROPIC_API_KEY",
            AiProvider.OpenRouter=>"OPENROUTER_API_KEY",
            AiProvider.LocalAi=>"LOCAL_OLLAMA_HOST",
            _=>""
        };
        return !string.IsNullOrEmpty(env)&&!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(env))?env:null;
    }

    static string KeyProperty(AiProvider provider) => provider.ToString().ToLowerInvariant() + "_key";

    static Dictionary<string, string> ReadConfig(string path)
    {
        if (!File.Exists(path))
            return new(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("The AI key configuration is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The AI key configuration is not valid JSON.", error);
        }
    }
}

internal sealed class AiPresetGeneration
{
    internal const string DefaultGeminiModel = "gemini-3.1-flash-lite";
    static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(90)};
    static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};
    readonly Catalog catalog;

    internal AiPresetGeneration(Catalog catalog)=>this.catalog=catalog;

    internal static async Task<IReadOnlyList<string>> DiscoverModelsAsync(AiProvider provider,string apiKey,CancellationToken cancellationToken)
    {
        if (provider == AiProvider.LocalAi)
        {
            var localModels = new List<string>();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "http://localhost:11434/v1/models");
                using var resp = await Http.SendAsync(req, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync(cancellationToken);
                    localModels.AddRange(ParseModelList(AiProvider.LocalAi, body));
                }
            }
            catch { }

            if (localModels.Count == 0)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, "http://localhost:1234/v1/models");
                    using var resp = await Http.SendAsync(req, cancellationToken);
                    if (resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync(cancellationToken);
                        localModels.AddRange(ParseModelList(AiProvider.LocalAi, body));
                    }
                }
                catch { }
            }

            if (localModels.Count > 0)
                return localModels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            return ["llama3.2", "mistral", "qwen2.5-coder", "deepseek-r1", "phi3"];
        }

        if(string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Enter an API key before checking provider access.",nameof(apiKey));

        var models=new List<string>();
        string? pageToken=null;
        int pages=0;
        do
        {
            using var request=CreateModelDiscoveryRequest(provider,apiKey,pageToken);
            using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
            string body=await response.Content.ReadAsStringAsync(cancellationToken);
            if(!response.IsSuccessStatusCode)
            {
                int code=(int)response.StatusCode;
                string message=ProviderError(body);
                if(code==401||code==403)
                    throw new HttpRequestException($"No se pudo validar la clave de {provider} (HTTP {code}): {message}",null,response.StatusCode);
                if(code==429)
                    throw new HttpRequestException($"El proveedor {provider} rechazó la consulta por cuota o límite de peticiones (HTTP 429).",null,response.StatusCode);
                throw new HttpRequestException($"No se pudo consultar los modelos de {provider} (HTTP {code}): {message}",null,response.StatusCode);
            }

            var page=ParseModelListPage(provider,body);
            models.AddRange(page.Models);
            pageToken=page.NextPageToken;
            pages++;
        }
        while(!string.IsNullOrWhiteSpace(pageToken)&&pages<10&&models.Count<1000);

        string[] available=models.Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if(available.Length==0)
            throw new InvalidDataException($"The {provider} API key was accepted, but no usable generation models were returned for this account.");
        if(provider==AiProvider.Gemini)
            available=available.OrderBy(model=>model==DefaultGeminiModel?0:1).ThenBy(model=>model,StringComparer.OrdinalIgnoreCase).ToArray();
        return available;
    }

    internal static IReadOnlyList<string> ParseModelList(AiProvider provider,string body) =>
        ParseModelListPage(provider,body).Models;

    static (IReadOnlyList<string> Models,string? NextPageToken) ParseModelListPage(AiProvider provider,string body)
    {
        try
        {
            using var document=JsonDocument.Parse(body);
            var root=document.RootElement;
            if(!root.TryGetProperty("data",out var data)&&!root.TryGetProperty("models",out data))
                throw new InvalidDataException($"The {provider} model-list response did not contain a model collection.");
            if(data.ValueKind!=JsonValueKind.Array)
                throw new InvalidDataException($"The {provider} model-list response was not an array.");

            var models=new List<string>();
            foreach(var item in data.EnumerateArray())
            {
                if(!item.TryGetProperty("id",out var idElement)&&!item.TryGetProperty("name",out idElement))
                    continue;
                string? id=idElement.GetString();
                if(string.IsNullOrWhiteSpace(id))
                    continue;
                if(provider==AiProvider.Gemini)
                {
                    if(id.StartsWith("models/",StringComparison.Ordinal))
                        id=id["models/".Length..];
                    if(!item.TryGetProperty("supportedGenerationMethods",out var methods)
                        ||methods.ValueKind!=JsonValueKind.Array
                        ||!methods.EnumerateArray().Any(method=>method.GetString()=="generateContent"))
                        continue;
                }
                models.Add(id);
            }

            string? nextPageToken=null;
            if(provider==AiProvider.Anthropic&&root.TryGetProperty("has_more",out var hasMore)&&hasMore.ValueKind==JsonValueKind.True
                &&root.TryGetProperty("last_id",out var lastId)&&lastId.ValueKind==JsonValueKind.String)
                nextPageToken=lastId.GetString();
            else if(provider==AiProvider.Gemini&&root.TryGetProperty("nextPageToken",out var nextPage)&&nextPage.ValueKind==JsonValueKind.String)
                nextPageToken=nextPage.GetString();

            return (models,nextPageToken);
        }
        catch(JsonException error)
        {
            throw new InvalidDataException($"The {provider} model-list response was not valid JSON.",error);
        }
    }

    internal static HttpRequestMessage CreateModelDiscoveryRequest(AiProvider provider,string apiKey,string? pageToken=null)
    {
        string query=string.IsNullOrWhiteSpace(pageToken)?"":provider switch
        {
            AiProvider.Gemini=>"&pageToken="+Uri.EscapeDataString(pageToken),
            AiProvider.Anthropic=>"&after_id="+Uri.EscapeDataString(pageToken),
            _=>""
        };
        var request=provider switch
        {
            AiProvider.Gemini=>new HttpRequestMessage(HttpMethod.Get,"https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000"+query),
            AiProvider.Anthropic=>new HttpRequestMessage(HttpMethod.Get,"https://api.anthropic.com/v1/models?limit=1000"+query),
            AiProvider.OpenRouter=>new HttpRequestMessage(HttpMethod.Get,"https://openrouter.ai/api/v1/models/user"),
            AiProvider.LocalAi=>new HttpRequestMessage(HttpMethod.Get,"http://localhost:11434/v1/models"),
            _=>throw new ArgumentOutOfRangeException(nameof(provider))
        };
        switch(provider)
        {
            case AiProvider.Gemini:
                request.Headers.TryAddWithoutValidation("x-goog-api-key",apiKey);
                break;
            case AiProvider.Anthropic:
                request.Headers.TryAddWithoutValidation("x-api-key",apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version","2023-06-01");
                break;
            case AiProvider.OpenRouter:
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",apiKey.Trim());
                request.Headers.TryAddWithoutValidation("HTTP-Referer","https://github.com/Goaltoday/GP200_Custom_Editor");
                request.Headers.TryAddWithoutValidation("X-Title","GP200 Replica");
                break;
            case AiProvider.LocalAi:
                break;
        }
        return request;
    }

    internal async Task<IReadOnlyList<AiGeneratedPreset>> GenerateAsync(
        AiProvider provider,string model,string apiKey,AiPresetMode mode,string description,
        PresetDocument? current,IReadOnlyList<PresetDocument> deviceLibrary,bool includeFullBankParameters,
        AiPerformanceMode performanceMode,Mx42SceneSnapshot? sceneSnapshot,IReadOnlyList<string> requestedSections,bool webSearch,CancellationToken cancellationToken,
        byte[]? audioBytes=null,string? audioMimeType=null,AudioToneReport? audioReport=null,
        string? songUrl=null,AiChainTopology chainTopology=AiChainTopology.Auto,int maxNewSongPresets=9)
    {
        if(string.IsNullOrWhiteSpace(model))throw new ArgumentException("Select a model.",nameof(model));
        if(string.IsNullOrWhiteSpace(description))throw new ArgumentException("Describe the sound you want.",nameof(description));
        if(maxNewSongPresets is <1 or >9)throw new ArgumentOutOfRangeException(nameof(maxNewSongPresets),"New-song output is limited to one through nine presets.");

        if(performanceMode==AiPerformanceMode.Scenes&&!catalog.IsMx42)
            throw new NotSupportedException("Scene generation requires the MX42 profile.");
        if(performanceMode==AiPerformanceMode.Scenes&&sceneSnapshot!=null&&current!=null&&current.Index!=sceneSnapshot.Slot)
            throw new InvalidOperationException("The selected preset does not match the live Scene-bank slot.");

        if(includeFullBankParameters&&(deviceLibrary.Count!=256||!deviceLibrary.Select(p=>(int)p.Index).Order().SequenceEqual(Enumerable.Range(0,256))))
            throw new InvalidDataException("Full-bank AI context requires a complete, verified set of all 256 device presets.");

        PresetDocument seed=current==null?ReadTemplate():new PresetDocument(current.ToBytes());

        string? effectiveKey=AiApiKeyResolver.ResolveKey(provider,apiKey);
        if(string.IsNullOrWhiteSpace(effectiveKey))
        {
            throw new InvalidOperationException($"Introduce tu clave API para {provider} o configúrala en las opciones / variables de entorno ($env:GEMINI_API_KEY).");
        }

        string context=BuildContext(current,deviceLibrary,includeFullBankParameters);
        string instructions=BuildInstructions(mode,performanceMode,description,context,audioReport,songUrl,chainTopology,maxNewSongPresets);
        var response=await RequestAsync(provider,model,effectiveKey,instructions,webSearch,cancellationToken,audioBytes,audioMimeType);
        string json=ExtractJson(response.Text);
        AiPlan? plan = null;
        try
        {
            plan = JsonSerializer.Deserialize<AiPlan>(json, JsonOptions);
        }
        catch(JsonException)
        {
            // Attempt to parse as a direct array of preset objects [ {...}, {...} ]
            try
            {
                var presetsList = JsonSerializer.Deserialize<List<AiPresetPlan>>(json, JsonOptions);
                if (presetsList != null && presetsList.Count > 0)
                {
                    plan = new AiPlan { Presets = presetsList };
                }
            }
            catch { }

            // Attempt to parse as a single preset object { "name": ..., "effects": [...] }
            if (plan == null)
            {
                try
                {
                    var singlePreset = JsonSerializer.Deserialize<AiPresetPlan>(json, JsonOptions);
                    if (singlePreset != null && (singlePreset.Effects != null || !string.IsNullOrEmpty(singlePreset.Name)))
                    {
                        plan = new AiPlan { Presets = new List<AiPresetPlan> { singlePreset } };
                    }
                }
                catch { }
            }
        }

        // If the AI output contained unparseable text or empty proposals, generate a safe musical fallback
        if (plan?.Presets == null || plan.Presets.Count == 0)
        {
            plan = new AiPlan
            {
                Presets = new List<AiPresetPlan>
                {
                    new AiPresetPlan
                    {
                        Name = SanitizeName(description.Length > 14 ? description[..14] : description),
                        Notes = "AI Tone generated for GP-200"
                    }
                }
            };
        }

        // Flexibly clamp to the requested maximum without throwing errors
        if (plan.Presets.Count > maxNewSongPresets)
        {
            plan.Presets = plan.Presets.Take(maxNewSongPresets).ToList();
        }
        if (plan.Presets.Count > 9)
        {
            plan.Presets = plan.Presets.Take(9).ToList();
        }

        if(mode==AiPresetMode.NewSong&&performanceMode!=AiPerformanceMode.Scenes)
            ValidateSections(plan.Presets.Select(preset=>preset.Section).ToArray(),requestedSections,maxNewSongPresets);

        if(performanceMode==AiPerformanceMode.Scenes&&plan.Presets.Count>1)
            plan.Presets = plan.Presets.Take(1).ToList();

        var result=new List<AiGeneratedPreset>(plan.Presets.Count);
        foreach(var preset in plan.Presets)
        {
            if(string.IsNullOrWhiteSpace(preset.Name)) preset.Name = "AI Preset";
            var document=BuildPreset(seed,preset,performanceMode,chainTopology);
            document=ToneSanitizer.ValidateAndFixMusicality(document,catalog);
            ScenePresetBundle? scenes=null;
            if(performanceMode==AiPerformanceMode.Scenes)
            {
                // Ensure exactly 4 scenes are present flexibly without throwing
                if (preset.Scenes == null) preset.Scenes = new List<AiScenePlan>();
                while (preset.Scenes.Count < 4)
                {
                    int sIdx = preset.Scenes.Count;
                    string sName = sIdx switch
                    {
                        0 => "Scene A - Clean",
                        1 => "Scene B - Crunch",
                        2 => "Scene C - Lead",
                        _ => "Scene D - Ambient"
                    };
                    preset.Scenes.Add(new AiScenePlan { Name = sName, Changes = new List<AiSceneChangePlan>() });
                }
                if (preset.Scenes.Count > 4)
                {
                    preset.Scenes = preset.Scenes.Take(4).ToList();
                }

                var sceneDocuments=preset.Scenes.Select(scene=>ToneSanitizer.ValidateAndFixMusicality(ApplySceneChanges(document,scene),catalog)).ToArray();
                var generatedSnapshot=sceneSnapshot!=null
                    ?sceneSnapshot.WithGeneratedStates(document,sceneDocuments)
                    :Mx42SceneSnapshot.CreateCompleteSnapshot(document,sceneDocuments,catalog);
                var bundle=Mx42PresetBundle.Create(sceneDocuments[0],generatedSnapshot);
                scenes=ScenePresetBundle.Wrap(bundle);scenes.Validate();
            }
            var refinedFrom=mode==AiPresetMode.NewSong||current==null?null:new PresetDocument(current.ToBytes());
            result.Add(new(SanitizeName(preset.Name),document,scenes,response.Sources,preset.Section,refinedFrom));
        }
        return result;
    }

    string BuildContext(PresetDocument? current,IReadOnlyList<PresetDocument> deviceLibrary,bool includeFullBankParameters)
    {
        var algorithmGroups=new List<object>();
        if(catalog.IsMx42)
        {
            for(int family=0;family<Catalog.FamilyNames.Length;family++)
            {
                int naturalSlot = Math.Min(family, 10);
                var algorithms=catalog.ForFamily(naturalSlot,family).Where(a=>!a.IsNone&&a.ParametersKnown).Select(AlgorithmContext).ToArray();
                algorithmGroups.Add(new{family=Catalog.FamilyNames[family],algorithms});
            }
        }
        else
        {
            for(int module=0;module<PresetDocument.Modules.Length;module++)
            {
                var algorithms=catalog.ForModule(module).Where(a=>a.ParametersKnown).Select(AlgorithmContext).ToArray();
                algorithmGroups.Add(new{module=PresetDocument.Modules[module],algorithms});
            }
        }

        object[] devicePresets = includeFullBankParameters
            ? deviceLibrary.Select(p =>
            {
                var effects = Enumerable.Range(0, 11).Select(module =>
                {
                    var effect = p.GetEffect(module); var algorithm = catalog.Find(module, effect.Code);
                    return new
                    {
                        module = PresetDocument.Modules[module],
                        enabled = effect.Enabled,
                        code = effect.Code.ToString("X8"),
                        algorithm = algorithm?.Name ?? "unknown",
                        parameters = effect.Parameters.Select(FiniteOrNull).ToArray()
                    };
                }).ToArray();
                return (object)new
                {
                    slot = p.Index,
                    name = p.Name,
                    volume = p.Volume,
                    tempo = p.Tempo,
                    pan = p.Pan,
                    chain = p.Chain.Select(x => PresetDocument.Modules[x]).ToArray(),
                    fxLoopMode = p.FxLoopMode,
                    sendReturn = p.RoutingRaw,
                    effects
                };
            }).ToArray()
            : [];

        object? active=current==null?null:new
        {
            name=current.Name,volume=current.Volume,tempo=current.Tempo,
            chain=current.Chain.Select(x=>PresetDocument.Modules[x]).ToArray(),
            fxLoopMode=current.FxLoopMode,
            dualChain=catalog.IsMx42&&ChainRouting.Extended(current.FxLoopMode)?Mx42ChainConfig.GetOrDefault(current):null,
            effects=Enumerable.Range(0,11).Select(module=>
            {
                var effect=current.GetEffect(module);var algorithm=catalog.Find(module,effect.Code);
                return new{module=PresetDocument.Modules[module],enabled=effect.Enabled,code=effect.Code.ToString("X8"),algorithm=algorithm?.Name??"unknown"};
            }).ToArray()
        };

        return JsonSerializer.Serialize(new
        {
            deviceProfile=catalog.Profile.ToString(),
            mx42Capabilities=DeviceSession.Current.Capabilities,
            algorithms=algorithmGroups,
            controlSlots=(current??ReadTemplate()).Controls.Select(control=>new{slot=control.Slot,currentMask=control.Mask}).ToArray(),
            activePreset=active,
            loadedDevicePresets=devicePresets,
            devicePresetCount=deviceLibrary.Count,
            fullBankParameterValuesIncluded=includeFullBankParameters
        });
    }

    static object AlgorithmContext(AlgorithmInfo algorithm)=>new
    {
        code=algorithm.Code.ToString("X8"),
        name=algorithm.Name,
        knobs=algorithm.Parameters.Select(p=>p.Name).ToArray()
    };

    static double? FiniteOrNull(float value)=>float.IsFinite(value)?value:null;

    internal static void ValidateSections(IReadOnlyList<string?> proposalSections,IReadOnlyList<string> requestedSections,int maxNewSongPresets)
    {
        if (proposalSections.Count < 1)
            throw new InvalidDataException("The AI response must contain at least one preset.");

        if (proposalSections.Count > maxNewSongPresets)
            throw new InvalidDataException($"The AI must choose between one and {maxNewSongPresets} presets for this set.");
    }

    string BuildInstructions(AiPresetMode mode,AiPerformanceMode performanceMode,string description,string context,AudioToneReport? audioReport=null,string? songUrl=null,AiChainTopology chainTopology=AiChainTopology.Auto,int maxNewSongPresets=9)
    {
        string profileLimits=catalog.IsMx42
            ?Mx42Integration.Supports(Mx42Capability.Chain)
                ?"MX42 Custom profile active. Dual routing and universal blocks are supported. Use only algorithm code values listed in the supplied catalog."
                :"MX42 Custom profile active. Use only algorithm code values listed in the supplied catalog."
            :"Stock profile. Use only algorithm codes listed for that exact module.";

        string audioSection=audioReport!=null
            ?$"\nLocal Audio DSP Analysis: {audioReport.EstimatedGainCharacter}\n"
            :"";

        string topologyDirective=chainTopology switch
        {
            AiChainTopology.DualChain=>"\nSignal Chain Topology: User requested DUAL CHAIN A/B. Set dualChain: { enabled: true, send: 4, split: 6, return: 8, blend: 50, align: 0, inverted: false }.\n",
            AiChainTopology.Series=>"\nSignal Chain Topology: User requested SERIES chain. Set dualChain: { enabled: false }.\n",
            _=>"\nSignal Chain Topology: Choose Series or Dual Chain A/B based on the required tone.\n"
        };

        return """
            Create GP-200 preset proposals from the supplied pedal catalog.
            Return only JSON matching:
            {"presets":[{"name":"ASCII, at most 16 characters","section":"Clean","volume":80,"tempo":120,"chain":[0,1,2,3,4,5,6,7,8,9,10],"effects":[{"module":0,"code":"00000000","enabled":true,"parameters":{"0":0.5}}],"controlMasks":[],"dualChain":{"enabled":false,"send":4,"split":6,"return":8,"blend":50,"align":0,"inverted":false},"scenes":[{"name":"A","changes":[{"module":0,"enabled":true,"parameters":{"0":0.5}}]}],"expressionAssignments":[],"notes":"short ASCII note"}]}
            Each proposal must define all eleven effects exactly once, with module indices 0 through 10. The chain must contain each module index exactly once. These are fixed physical preset slots; duplicate instances of a module are not supported by the GP-200 preset format and must not be generated. MX42 permits at most one selected NAM block in a preset. For NewSong outside Scenes, include a section label (Clean, Crunch, Dist, or Lead) and return between 1 and 3 proposals (each labeled with its musical section); for Refine, return one to three variations. For Scenes, return exactly one preset with exactly four scene variations.
            Every effect code must be an exact eight-hex-digit code from the catalog and valid for that module. Volume must be 0..100 and tempo 40..250.
            Control masks are per-preset CTRL state only. Scene banks must preserve effect IDs, chain order, and CHAIN topology; each scene changes only declared parameters and bypass state.

            GP-200 SOUND ENGINEERING LAWS (MUST BE ENFORCED):
            1. Canonical Series Chain Order: Arrange the 11 physical modules musically: chain: [1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10] -> WAH(1) -> PRE(0) -> DST(2) -> AMP(3) -> NR(4) -> CAB(5) -> EQ(6) -> MOD(7) -> DLY(8) -> RVB(9) -> VOL(10). Cabinet (CAB) must NEVER be placed before Amplifier (AMP).
            2. Chain Routing: Default to SERIES (dualChain: { enabled: false }). Only use dualChain if dual amp routing is strictly requested.
            3. Gain Staging: Clean/Worship/Crunch (AMP Gain 30..55, DST Gain 15..35, DST Vol 60..70). High-Gain/Metal/Lead (AMP Gain 55..72, Green OD Gain 10..20 Tone 60 Level 75, and MUST enable NR with threshold 35..50).
            4. Cabinet Filtering: If AMP is enabled, CAB must be enabled (unless full-rig NAM). When CAB is enabled, set Low Cut to 80..110 Hz and High Cut to 7500..9500 Hz to prevent harsh fizz.
            5. Time-based Effects: Delay Mix max 25..35%, Reverb Mix max 20..40%.
            """ + "\n" + profileLimits + "\n" + topologyDirective + "\n" +
            "Target performance mode: "+performanceMode+". "+
            (performanceMode switch
            {
                AiPerformanceMode.Stomp=>"Prepare per-preset CTRL states suitable for a stomp-style performance, using controlMasks only.",
                AiPerformanceMode.User=>"Prepare per-preset CTRL states suitable for a user footswitch setup, using controlMasks only.",
                AiPerformanceMode.Scenes=>"Return exactly one preset and exactly four scene variations (A: Clean/Ambient, B: Crunch/Rhythm, C: High-Gain/Drive, D: Lead/Solo Boost). Do not change any effect algorithm, block order, or dual-chain topology; each scene may change only declared parameters and bypass states.",
                _=>"Prepare a normal patch-mode preset."
            })+
            (mode==AiPresetMode.NewSong&&maxNewSongPresets<9
                ?$"\nAI-chosen set size: Choose between 1 and {maxNewSongPresets} distinct presets. Label every proposal with its musical section.\n"
                :"")+
            (mode switch
            {
                AiPresetMode.OptimizeForLive => " Optimize this preset specifically for live band performance: Balance the EQ curve (cut sub-bass below 80 Hz and harsh highs above 6.5 kHz), balance gain and overall volume, and moderate time-based effects (reverbs and delays) so the guitar cuts cleanly through a dense live mix.",
                AiPresetMode.Refine => " Refine the active preset while preserving its intent unless the user asks otherwise.",
                _ => " Build a new song-oriented sound; make variations musically distinct but compatible with the supplied pedal catalog."
            })+
            audioSection+
            "\nUser request:\n"+description+"\n\nPedal catalog context:\n"+context;
    }

    async Task<AiResponse> RequestAsync(AiProvider provider,string model,string apiKey,string prompt,bool webSearch,CancellationToken token,byte[]? audioBytes=null,string? audioMimeType=null)
    {
        if (provider != AiProvider.Gemini)
        {
            return await ExecuteRequestAsync(provider, model, apiKey, prompt, webSearch, token, audioBytes, audioMimeType);
        }

        // Auto-migrar referencias obsoletas de gemini-1.5 / gemini-2.5 a los modelos actuales de Google AI Studio
        string initialModel = model;
        if (string.IsNullOrWhiteSpace(initialModel) || initialModel.Contains("1.5", StringComparison.OrdinalIgnoreCase))
            initialModel = "gemini-3.1-flash-lite";

        // Modelos verificados con cuota activa en Google AI Studio (gemini-3.1-flash-lite prioritario)
        string[] candidates = [initialModel, "gemini-3.1-flash-lite", "gemini-2.5-flash-lite", "gemini-3.5-flash", "gemini-2.5-flash"];
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Exception? lastError = null;
        HttpRequestException? lastQuotaError = null;

        foreach (var candidate in candidates)
        {
            if (!tried.Add(candidate)) continue;
            try
            {
                return await ExecuteRequestAsync(provider, candidate, apiKey, prompt, webSearch, token, audioBytes, audioMimeType);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                lastQuotaError = ex;
                lastError = ex;
                try { await Task.Delay(2500, token); } catch { }
                continue;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                lastError = ex;
                continue;
            }
        }

        if (lastQuotaError != null)
            throw new HttpRequestException($"Has alcanzado el límite de cuota (HTTP 429) de tu clave de Gemini en Google AI Studio. Prueba con 'gemini-3.1-flash-lite' o usa OpenRouter / Anthropic.", null, System.Net.HttpStatusCode.TooManyRequests);

        throw lastError ?? new HttpRequestException("No se pudo conectar con el proveedor de IA. Comprueba tu clave de API.");
    }

    async Task<AiResponse> ExecuteRequestAsync(AiProvider provider,string model,string apiKey,string prompt,bool webSearch,CancellationToken token,byte[]? audioBytes=null,string? audioMimeType=null)
    {
        if (provider == AiProvider.LocalAi)
        {
            HttpRequestException? lastEx = null;
            string[] endpoints = ["http://localhost:11434/v1/chat/completions", "http://localhost:1234/v1/chat/completions"];
            foreach (var url in endpoints)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Content = JsonContent(new
                    {
                        model,
                        temperature = 0.3,
                        response_format = new { type = "json_object" },
                        messages = new[]
                        {
                            new { role = "system", content = "You are an expert audio engineer and guitar technician for the Valeton GP-200. Return valid JSON only matching the schema exactly." },
                            new { role = "user", content = prompt }
                        }
                    });
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                    if (resp.IsSuccessStatusCode)
                    {
                        string b = await resp.Content.ReadAsStringAsync(token);
                        using var d = JsonDocument.Parse(b);
                        string t = d.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                        return new(t, []);
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastEx = ex;
                }
            }
            throw new HttpRequestException("No se pudo conectar con el servidor de IA local (Ollama en puerto 11434 o LM Studio en puerto 1234). Asegúrate de que Ollama o LM Studio esté iniciado en tu PC.", lastEx);
        }

        using var request=provider switch
        {
            AiProvider.Gemini=>GeminiRequest(model,apiKey,prompt,webSearch,audioBytes,audioMimeType),
            AiProvider.Anthropic=>AnthropicRequest(model,apiKey,prompt,webSearch),
            AiProvider.OpenRouter=>OpenRouterRequest(model,apiKey,prompt,webSearch),
            _=>throw new ArgumentOutOfRangeException(nameof(provider))
        };
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        string body=await response.Content.ReadAsStringAsync(token);
        if(!response.IsSuccessStatusCode)
        {
            int code=(int)response.StatusCode;
            string err=ProviderError(body);
            if(code==429)
                throw new HttpRequestException($"Has alcanzado el límite de cuota (HTTP 429 Too Many Requests) de tu clave de {provider}. Comprueba tu plan en la consola o prueba con 'gemini-2.5-flash-lite' (mayor cuota gratuita).",null,System.Net.HttpStatusCode.TooManyRequests);
            throw new HttpRequestException($"El proveedor de IA ({provider}) devolvió {code} ({response.ReasonPhrase}): {err}",null,response.StatusCode);
        }
        using var document=JsonDocument.Parse(body);
        string text=provider switch
        {
            AiProvider.Gemini=>document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()??"",
            AiProvider.Anthropic=>document.RootElement.GetProperty("content").EnumerateArray().First(x=>x.GetProperty("type").GetString()=="text").GetProperty("text").GetString()??"",
            AiProvider.OpenRouter=>document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()??"",
            _=>throw new ArgumentOutOfRangeException(nameof(provider))
        };
        return new(text,webSearch?ReadSources(provider,document.RootElement):[]);
    }

    internal static HttpRequestMessage GeminiRequest(string model,string key,string prompt,bool webSearch=false,byte[]? audioBytes=null,string? audioMimeType=null)
    {
        string cleanModel = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? model["models/".Length..] : model;
        var request=new HttpRequestMessage(HttpMethod.Post,"https://generativelanguage.googleapis.com/v1beta/models/"+Uri.EscapeDataString(cleanModel)+":generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key",key);

        var parts=new List<object>{new{text=prompt}};
        object body=new
        {
            contents=new[]{new{parts=parts.ToArray()}},
            generationConfig=new
            {
                responseMimeType="application/json",
                temperature=0.7,
                maxOutputTokens=2500
            }
        };
        request.Content=JsonContent(body);
        return request;
    }
    static HttpRequestMessage AnthropicRequest(string model,string key,string prompt,bool webSearch)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,"https://api.anthropic.com/v1/messages");
        request.Headers.TryAddWithoutValidation("x-api-key",key);request.Headers.TryAddWithoutValidation("anthropic-version","2023-06-01");
        request.Content=JsonContent(new{model,max_tokens=2500,messages=new[]{new{role="user",content=prompt}}});return request;
    }
    static HttpRequestMessage OpenRouterRequest(string model,string key,string prompt,bool webSearch)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,"https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key.Trim());
        request.Headers.TryAddWithoutValidation("HTTP-Referer","https://github.com/Goaltoday/GP200_Custom_Editor");
        request.Headers.TryAddWithoutValidation("X-Title","GP200 Replica");
        request.Content=JsonContent(new{model,temperature=0.4,response_format=new{type="json_object"},messages=new[]{new{role="user",content=prompt}}});return request;
    }
    static IReadOnlyList<AiSource> ReadSources(AiProvider provider,JsonElement root)
    {
        var sources=new List<AiSource>();
        void Add(string? title,string? url)
        {
            if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&sources.All(s=>s.Url!=uri.AbsoluteUri))
                sources.Add(new(string.IsNullOrWhiteSpace(title)?uri.Host:title,uri.AbsoluteUri));
        }
        if(provider==AiProvider.Gemini&&root.TryGetProperty("candidates",out var candidates)&&candidates.GetArrayLength()>0&&candidates[0].TryGetProperty("groundingMetadata",out var metadata)&&metadata.TryGetProperty("groundingChunks",out var chunks))
        {
            foreach(var chunk in chunks.EnumerateArray())if(chunk.TryGetProperty("web",out var web))
                Add(web.TryGetProperty("title",out var title)?title.GetString():null,web.TryGetProperty("uri",out var uri)?uri.GetString():null);
        }
        else if(provider==AiProvider.Anthropic&&root.TryGetProperty("content",out var content))
        {
            foreach(var block in content.EnumerateArray())
                if(block.TryGetProperty("content",out var results)&&results.ValueKind==JsonValueKind.Array)
                    foreach(var item in results.EnumerateArray())
                        if(item.TryGetProperty("url",out var url))Add(item.TryGetProperty("title",out var title)?title.GetString():null,url.GetString());
        }
        else if(provider==AiProvider.OpenRouter&&root.TryGetProperty("choices",out var choices)&&choices.GetArrayLength()>0&&choices[0].TryGetProperty("message",out var message)&&message.TryGetProperty("annotations",out var annotations))
        {
            foreach(var annotation in annotations.EnumerateArray())
                if(annotation.TryGetProperty("url_citation",out var citation))
                    Add(citation.TryGetProperty("title",out var title)?title.GetString():null,citation.TryGetProperty("url",out var url)?url.GetString():null);
        }
        return sources;
    }
    static StringContent JsonContent(object value)=>new(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json");
    static string ProviderError(string body)
    {
        try
        {
            using var json=JsonDocument.Parse(body);
            if(json.RootElement.TryGetProperty("error",out var error))
                return error.TryGetProperty("message",out var message)?message.GetString()??"Request failed.":"Request failed.";
        }
        catch(JsonException){}
        return body.Length>500?body[..500]:body;
    }
    static string ExtractJson(string response)
    {
        string text = response.Trim();
        int codeStart = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (codeStart >= 0)
        {
            int contentStart = text.IndexOf('\n', codeStart);
            if (contentStart >= 0)
            {
                int codeEnd = text.IndexOf("```", contentStart, StringComparison.Ordinal);
                if (codeEnd > contentStart)
                    return text[(contentStart + 1)..codeEnd].Trim();
            }
        }

        int genericCodeStart = text.IndexOf("```", StringComparison.Ordinal);
        if (genericCodeStart >= 0)
        {
            int contentStart = text.IndexOf('\n', genericCodeStart);
            if (contentStart >= 0)
            {
                int codeEnd = text.IndexOf("```", contentStart, StringComparison.Ordinal);
                if (codeEnd > contentStart)
                    return text[(contentStart + 1)..codeEnd].Trim();
            }
        }

        int firstBrace = text.IndexOf('{');
        int lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }

        int firstBracket = text.IndexOf('[');
        int lastBracket = text.LastIndexOf(']');
        if (firstBracket >= 0 && lastBracket > firstBracket)
        {
            return text.Substring(firstBracket, lastBracket - firstBracket + 1).Trim();
        }

        return text;
    }
    static PresetDocument ReadTemplate()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"Data","clear-default-gp200-1.8.2.prst");
        if(!File.Exists(path))throw new FileNotFoundException("The bundled GP-200 preset template is missing.",path);
        return new PresetDocument(File.ReadAllBytes(path));
    }
    static string SanitizeName(string? requested)
    {
        string name=new((requested??"AI PRESET").Where(c=>c is >= ' ' and <= '~').ToArray());
        name=name.Trim();if(name.Length==0)name="AI PRESET";
        if(name.Length>16)name=name[..16].TrimEnd();
        return name;
    }

    PresetDocument BuildPreset(PresetDocument seed,AiPresetPlan plan,AiPerformanceMode performanceMode,AiChainTopology chainTopology=AiChainTopology.Auto)
    {
        var document=new PresetDocument(seed.ToBytes());
        if(plan.Effects != null)
        {
            var seen=new HashSet<int>();
            foreach(var item in plan.Effects)
            {
                if(item.Module is <0 or >10 || !seen.Add(item.Module)) continue;

                uint code = document.GetEffect(item.Module).Code;
                if(!string.IsNullOrWhiteSpace(item.Code) && uint.TryParse(item.Code.Replace("0x","",StringComparison.OrdinalIgnoreCase),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint parsedCode))
                {
                    code = parsedCode;
                }

                var algorithm=catalog.Find(item.Module,code);
                if(algorithm==null)
                {
                    for(int m=0;m<11;m++)
                    {
                        var candidate=catalog.Find(m,code);
                        if(candidate!=null){algorithm=candidate;break;}
                    }
                }

                if(performanceMode==AiPerformanceMode.Scenes && document.GetEffect(item.Module).Code != code)
                {
                    algorithm = catalog.Find(item.Module, document.GetEffect(item.Module).Code);
                }

                bool runtimeModelUnavailable=algorithm!=null&&!RuntimeModelAvailable(item.Module,algorithm);
                if(algorithm?.ParametersKnown!=true||runtimeModelUnavailable)
                {
                    var fallback=catalog.ForModule(item.Module).FirstOrDefault(a=>a.ParametersKnown&&!a.IsNone)
                        ??catalog.Find(item.Module,document.GetEffect(item.Module).Code);

                    if(fallback!=null)
                    {
                        algorithm=fallback;
                    }
                    else
                    {
                        var existing=document.GetEffect(item.Module);
                        document.SetEffect(item.Module,item.Enabled ?? existing.Enabled,existing.Parameters);
                        continue;
                    }
                }

                document.SetAlgorithm(item.Module,algorithm);
                var effect=document.GetEffect(item.Module);
                foreach(var pair in item.Parameters??[])
                {
                    if(!int.TryParse(pair.Key,NumberStyles.None,CultureInfo.InvariantCulture,out int slot)||slot is <0 or >14)
                        continue;
                    if(Catalog.ControlWord(catalog,item.Module,slot))
                        continue;
                    var info=algorithm.Parameters.FirstOrDefault(p=>p.Slot==slot);
                    if(info==null||!float.IsFinite(pair.Value))
                        continue;

                    var (minimum,maximum)=info.Bounds(effect);
                    float val=Math.Clamp(pair.Value,minimum,maximum);
                    if(info.Kind=="Combox"&&info.Menus.Count>0&&!info.Menus.Any(menu=>menu.Value==val))
                    {
                        val=info.Menus.OrderBy(m=>Math.Abs(m.Value-val)).First().Value;
                    }
                    document.SetParameter(item.Module,slot,val,catalog);
                    effect=document.GetEffect(item.Module);
                }
                document.SetEffect(item.Module,item.Enabled ?? effect.Enabled,effect.Parameters);
            }
        }

        if(plan.Chain is {Length:11} && plan.Chain.Distinct().Count() == 11 && plan.Chain.All(x => x is >=0 and <=10))
        {
            document.SetChain(plan.Chain.Select(x=>(byte)x));
        }

        int volume = plan.Volume.HasValue ? Math.Clamp(plan.Volume.Value, 0, 100) : document.Volume;
        int tempo = plan.Tempo.HasValue ? Math.Clamp(plan.Tempo.Value, 40, 250) : document.Tempo;
        document.SetGeneral(SanitizeName(plan.Name ?? "Preset"),volume,tempo);
        document.SetMetadata("",SanitizeNotes(plan.Notes ?? ""));

        // Flexible CTRL Masks: Apply whatever the AI provided without rigid mode locks
        if(plan.ControlMasks is {Count:>0})
        {
            var slots=new HashSet<int>();
            foreach(var control in plan.ControlMasks)
            {
                if(control.Slot is >=0 and <=7 && slots.Add(control.Slot))
                {
                    uint mask = (uint)Math.Clamp(control.Mask, 0, 0x7ff);
                    document.SetCtrl((byte)control.Slot, mask);
                }
            }
        }

        // Flexible Expression Assignments
        if(plan.ExpressionAssignments is {Count:>0})
        {
            foreach(var exp in plan.ExpressionAssignments)
            {
                if(exp.Slot is (0 or 1) && exp.Module is >=0 and <=10 && exp.Parameter is >=0 and <=14)
                {
                    float end1 = exp.End1.HasValue && float.IsFinite(exp.End1.Value) ? exp.End1.Value : 100f;
                    float end0 = exp.End0.HasValue && float.IsFinite(exp.End0.Value) ? exp.End0.Value : 0f;
                    document.SetDestination(false,(byte)exp.Slot,(byte)exp.Module,(ushort)exp.Parameter,catalog);
                    document.SetExpression((byte)exp.Slot,end1,end0);
                }
            }
        }

        bool useDualChain = chainTopology == AiChainTopology.DualChain || (chainTopology == AiChainTopology.Auto && plan.DualChain?.Enabled == true);
        bool useSeries = chainTopology == AiChainTopology.Series || (chainTopology == AiChainTopology.Auto && plan.DualChain?.Enabled == false);

        if(useDualChain)
        {
            var dual=plan.DualChain;
            int send = Math.Clamp(dual?.Send ?? 4, 0, 11);
            int split = Math.Clamp(dual?.Split ?? 6, 0, 11);
            int ret = Math.Clamp(dual?.Return ?? 8, 0, 11);
            float blend = float.IsFinite(dual?.Blend ?? 50f) ? Math.Clamp(dual?.Blend ?? 50f, 0f, 100f) : 50f;
            int align = Math.Clamp(dual?.Align ?? 0, -32, 32);
            bool inverted = dual?.Inverted ?? false;

            if (send > split) send = split;
            if (ret < split) ret = split;

            ChainRouting.SetEnabled(document, true, split, 0, send, ret);
            ChainRouting.SetBlend(document, blend);
            ChainRouting.SetAlignment(document, align, inverted);
        }
        else if(useSeries)
        {
            ChainRouting.SetEnabled(document, false, 0, 0);
        }
        Mx42NamLimit.Validate(document);
        return document;
    }

    PresetDocument ApplySceneChanges(PresetDocument basePreset,AiScenePlan scene)
    {
        var document=new PresetDocument(basePreset.ToBytes());
        if(scene.Changes == null) return document;

        var modules=new HashSet<int>();
        foreach(var change in scene.Changes)
        {
            if(change.Module is <0 or >10 || !modules.Add(change.Module)) continue;
            var effect=document.GetEffect(change.Module);
            var algorithm=catalog.Find(change.Module,effect.Code);
            if(algorithm?.ParametersKnown!=true) continue;

            foreach(var pair in change.Parameters??[])
            {
                if(!int.TryParse(pair.Key,NumberStyles.None,CultureInfo.InvariantCulture,out int slot)||slot is <0 or >14)
                    continue;
                if(change.Module==10&&slot is 13 or 14)
                    continue;
                var info=algorithm.Parameters.FirstOrDefault(p=>p.Slot==slot);
                if(info==null || !float.IsFinite(pair.Value))
                    continue;

                var (minimum,maximum)=info.Bounds(effect);
                float val=Math.Clamp(pair.Value, minimum, maximum);
                if(info.Kind=="Combox"&&info.Menus.Count>0&&!info.Menus.Any(menu=>menu.Value==val))
                {
                    val=info.Menus.OrderBy(m=>Math.Abs(m.Value-val)).First().Value;
                }
                document.SetParameter(change.Module,slot,val,catalog);
                effect=document.GetEffect(change.Module);
            }
            if(change.Enabled.HasValue)
                document.SetEffect(change.Module,change.Enabled.Value,effect.Parameters);
        }
        return new PresetDocument(document.ToBytes());
    }

    bool RuntimeModelAvailable(int module,AlgorithmInfo algorithm)
    {
        if(catalog.IsMx42&&catalog.Identity(module,algorithm.Code) is {EffectiveFamily:11,IsNone:false} identity)
        {
            string label=catalog.Mx42NamLabels[identity.Ordinal];
            return !label.Contains("unknown",StringComparison.OrdinalIgnoreCase)&&
                !label.Contains("empty",StringComparison.OrdinalIgnoreCase)&&
                !label.Contains("vacío",StringComparison.OrdinalIgnoreCase);
        }
        if(algorithm.Name.Contains("SnapTone",StringComparison.OrdinalIgnoreCase))
            return catalog.DeviceSnapToneInventoryKnown&&
                !algorithm.Name.Contains("empty",StringComparison.OrdinalIgnoreCase)&&
                !algorithm.Name.Contains("vacío",StringComparison.OrdinalIgnoreCase);
        if(algorithm.Name.Contains("USER IR",StringComparison.OrdinalIgnoreCase))
            return catalog.DeviceIrInventoryKnown&&
                !algorithm.Name.Contains("empty",StringComparison.OrdinalIgnoreCase)&&
                !algorithm.Name.Contains("vacío",StringComparison.OrdinalIgnoreCase);
        return true;
    }

    static string SanitizeNotes(string? notes)
    {
        string result=new((notes??"AI-generated proposal").Where(c=>c is >= ' ' and <= '~').ToArray());
        return result.Length>40?result[..40]:result;
    }

    sealed class AiPlan { public List<AiPresetPlan>? Presets {get;set;} }
    sealed record AiResponse(string Text,IReadOnlyList<AiSource> Sources);
    sealed class AiPresetPlan
    {
        public string? Name {get;set;}
        public string? Section {get;set;}
        public int? Volume {get;set;}
        public int? Tempo {get;set;}
        public int[]? Chain {get;set;}
        public List<AiEffectPlan>? Effects {get;set;}
        public List<AiControlPlan>? ControlMasks {get;set;}
        public List<AiExpressionPlan>? ExpressionAssignments {get;set;}
        public AiDualChainPlan? DualChain {get;set;}
        public List<AiScenePlan>? Scenes {get;set;}
        public string? Notes {get;set;}
    }
    sealed class AiEffectPlan
    {
        public int Module {get;set;}
        public string? Code {get;set;}
        public bool? Enabled {get;set;}
        public Dictionary<string,float>? Parameters {get;set;}
    }
    sealed class AiExpressionPlan
    {
        public int Slot {get;set;}
        public int Module {get;set;}
        public int Parameter {get;set;}
        public float? End1 {get;set;}
        public float? End0 {get;set;}
    }
    sealed class AiDualChainPlan
    {
        public bool? Enabled {get;set;}
        public int? Send {get;set;}
        public int? Split {get;set;}
        public int? Return {get;set;}
        public float? Blend {get;set;}
        public int? Align {get;set;}
        public bool? Inverted {get;set;}
    }
    sealed class AiControlPlan
    {
        public int Slot {get;set;}
        public int Mask {get;set;}
    }
    sealed class AiScenePlan
    {
        public string? Name {get;set;}
        public List<AiSceneChangePlan>? Changes {get;set;}
    }
    sealed class AiSceneChangePlan
    {
        public int Module {get;set;}
        public bool? Enabled {get;set;}
        public Dictionary<string,float>? Parameters {get;set;}
    }
}
