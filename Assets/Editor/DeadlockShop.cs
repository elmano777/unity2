using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Step 28: the shop board in each base, laid out like the shop of the game the project is based on
/// (RF-11). An old paper board in a dark wooden frame with gold trim: coloured category tabs on the
/// left, a header plaque and the player's souls along the top, the items of the open tab grouped
/// in blocks by tier with the price on a tag at each block's corner, and a dark panel with the
/// chosen item's details where the original puts its last tier. The player's hero stands beside
/// it. Icons and text are our own (<see cref="DeadlockShopIcons"/>). Replaces the step 27 stands;
/// safe to re-run, and step 27 calls it too.
/// </summary>
public static class DeadlockShop
{
    private const string ScenePath = "Assets/Scenes/Lane.unity";
    private const string ShopFolder = "Assets/Data/Shop";
    private const string MaterialDir = "Assets/Art/Materials/Shop";
    private const string SevenPath = "Assets/Data/Heroes/Seven.asset";
    private const string BuyClipPath = "Assets/Audio/Combat/Hit.wav";

    // Board size and where things sit on it, in metres in the board's local space (+Y up, the
    // player on the -Z side looking at it).
    private const float PaperWidth = 2.6f, PaperHeight = 1.5f, PaperCenterY = 1.72f;

    // Content below the header: the tier blocks fill the left column top to bottom, the details
    // take the right column.
    private const float ContentTop = 2.16f, ContentBottom = 1.03f, TierGap = 0.09f;
    private const float TiersLeft = -1.22f, TiersRight = 0.34f;
    private const float DetailLeft = 0.42f, DetailRight = 1.22f;

    private static readonly Color Ink = new Color(0.2f, 0.13f, 0.08f, 1f);
    private static readonly Color Gold = new Color(1f, 0.8f, 0.45f, 1f);
    private static readonly Color Cream = new Color(1f, 0.88f, 0.7f, 1f);

    private static Material paper, block, card, ink, dark, wood, gold, selectedMat, dimMat, sealedMat, tagMat, inkText;
    private static readonly Dictionary<ShopCategory, Material[]> tabMats = new Dictionary<ShopCategory, Material[]>();
    private static readonly Dictionary<ShopItemSO, Material> iconMats = new Dictionary<ShopItemSO, Material>();

    private static readonly string[] ItemFiles =
    {
        "Item_Balas", "Item_Corta", "Item_Esbirros", "Item_Rafaga", "Item_LargoAlcance", "Item_Gatillo",
        "Item_EspirituExtra", "Item_Expansion", "Item_EspirituMejorado", "Item_Enfriamiento",
        "Item_Vitalidad", "Item_Botas", "Item_Regeneracion", "Item_Coraza", "Item_RoboVida",
    };

    [MenuItem("Deadlock/Setup/28 Shop Board (Avance 4)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (!BuildInOpenScene())
        {
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Deadlock Shop] Shop boards built in both bases.");
    }

    /// <summary>
    /// The five items from step 27, loaded fresh: opening a scene unloads assets nothing references,
    /// so items loaded before it may already be gone.
    /// </summary>
    private static ShopItemSO[] LoadItems()
    {
        return ItemFiles
            .Select(f => AssetDatabase.LoadAssetAtPath<ShopItemSO>($"{ShopFolder}/{f}.asset"))
            .Where(i => i != null)
            .ToArray();
    }

    /// <summary>Tab and tier of each of the five items, and their icons.</summary>
    public static void ConfigureItems(ShopItemSO[] items)
    {
        foreach (ShopItemSO item in items)
        {
            string file = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(item));

            switch (file)
            {
                // Weapon
                case "Item_Balas": Set(item, ShopCategory.Weapon, 1, DeadlockShopIcons.Bullets); break;
                case "Item_Corta": Set(item, ShopCategory.Weapon, 1, DeadlockShopIcons.Bullseye); break;
                case "Item_Esbirros": Set(item, ShopCategory.Weapon, 1, DeadlockShopIcons.Skull); break;
                case "Item_Rafaga": Set(item, ShopCategory.Weapon, 1, DeadlockShopIcons.FastBullet); break;
                case "Item_LargoAlcance": Set(item, ShopCategory.Weapon, 2, DeadlockShopIcons.Scope); break;
                case "Item_Gatillo": Set(item, ShopCategory.Weapon, 2, DeadlockShopIcons.Crosshair); break;

                // Spirit
                case "Item_EspirituExtra": Set(item, ShopCategory.Spirit, 1, DeadlockShopIcons.Star); break;
                case "Item_Expansion": Set(item, ShopCategory.Spirit, 1, DeadlockShopIcons.Waves); break;
                case "Item_EspirituMejorado": Set(item, ShopCategory.Spirit, 2, DeadlockShopIcons.StarPair); break;
                case "Item_Enfriamiento": Set(item, ShopCategory.Spirit, 2, DeadlockShopIcons.Hourglass); break;

                // Vitality
                case "Item_Vitalidad": Set(item, ShopCategory.Vitality, 1, DeadlockShopIcons.HeartPlus); break;
                case "Item_Botas": Set(item, ShopCategory.Vitality, 1, DeadlockShopIcons.Boot); break;
                case "Item_Regeneracion": Set(item, ShopCategory.Vitality, 1, DeadlockShopIcons.Regen); break;
                case "Item_Coraza": Set(item, ShopCategory.Vitality, 2, DeadlockShopIcons.Shield); break;
                case "Item_RoboVida": Set(item, ShopCategory.Vitality, 2, DeadlockShopIcons.Drop); break;
            }
        }
    }

    /// <summary>Official icons (Assets/Art/Shop/WikiIcons) for the items that have one; the others keep the drawn glyph.</summary>
    private static readonly Dictionary<string, string> WikiIcons = new Dictionary<string, string>
    {
        { "Item_Corta", "close_quarters" },
        { "Item_Esbirros", "monster_rounds" },
        { "Item_Rafaga", "rapid_rounds" },
        { "Item_LargoAlcance", "long_range" },
        { "Item_EspirituExtra", "extra_spirit" },
        { "Item_Expansion", "mystic_expansion" },
        { "Item_EspirituMejorado", "improved_spirit" },
        { "Item_Enfriamiento", "compress_cooldown" },
        { "Item_Vitalidad", "extra_health" },
        { "Item_Botas", "sprint_boots" },
        { "Item_Regeneracion", "extra_regen" },
        { "Item_Coraza", "battle_vest" },
        { "Item_RoboVida", "bullet_lifesteal" },
    };

    private static void Set(ShopItemSO item, ShopCategory category, int tier, System.Func<Vector2, float> glyph)
    {
        item.category = category;
        item.tier = tier;

        Sprite wiki = WikiIcons.TryGetValue(item.name, out string file)
            ? AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Shop/WikiIcons/{file}.png")
            : null;

        item.icon = wiki != null ? wiki : DeadlockShopIcons.ItemIcon($"Icon_{item.name}", category, glyph);
        EditorUtility.SetDirty(item);
    }

    /// <summary>Replaces the shops of the open Lane with a board in each base. False if step 27 hasn't made the items.</summary>
    public static bool BuildInOpenScene()
    {
        ShopItemSO[] items = LoadItems();

        if (items.Length == 0)
        {
            Debug.LogError("[Deadlock Shop] No shop items in Assets/Data/Shop. Run step 27 first.");
            return false;
        }

        ConfigureItems(items);
        CreateMaterials(items);

        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "Shop_Amber" || root.name == "Shop_Sapphire")
            {
                Object.DestroyImmediate(root);
            }
        }

        // Same spots as the step 27 stands: beside the spawn, turned towards it.
        BuildBoard("Shop_Amber", Faction.Player, new Vector3(5f, 0f, -73.5f), new Vector3(0f, 0f, -76f), items);
        BuildBoard("Shop_Sapphire", Faction.Enemy, new Vector3(-5f, 0f, 73.5f), new Vector3(0f, 0f, 76f), items);
        return true;
    }

    // ------------------------------------------------------------------ materials

    private static void CreateMaterials(ShopItemSO[] items)
    {
        DeadlockSceneKit.EnsureFolder(MaterialDir);
        Texture2D paperTex = DeadlockShopIcons.Paper();
        Texture2D tagTex = DeadlockShopIcons.PriceTag();

        paper = Unlit("Shop_Paper", Color.white, paperTex);
        block = Unlit("Shop_Block", new Color(0.86f, 0.82f, 0.76f), paperTex);
        card = Unlit("Shop_Card", new Color(0.97f, 0.93f, 0.83f));
        ink = Unlit("Shop_Ink", new Color(0.32f, 0.21f, 0.12f));
        dark = Unlit("Shop_Dark", new Color(0.11f, 0.085f, 0.07f));
        gold = Unlit("Shop_Gold", new Color(0.86f, 0.66f, 0.32f));
        selectedMat = Unlit("Shop_Selected", new Color(0.08f, 0.55f, 0.66f));
        tagMat = Unlit("Shop_Tag", Color.white, tagTex);
        dimMat = Unlit("Shop_Dim", new Color(0.5f, 0.48f, 0.45f, 0.62f), transparent: true);
        sealedMat = Unlit("Shop_Sealed", new Color(0.06f, 0.05f, 0.04f, 0.85f), transparent: true);
        wood = DeadlockSceneKit.Lit("Shop_Wood", new Color(0.2f, 0.12f, 0.07f), 0.35f);
        inkText = InkTextMaterial();

        tabMats.Clear();
        TabMaterials(ShopCategory.Weapon, DeadlockShopIcons.Crosshair);
        TabMaterials(ShopCategory.Vitality, DeadlockShopIcons.Plus);
        TabMaterials(ShopCategory.Spirit, DeadlockShopIcons.Star);

        iconMats.Clear();

        foreach (ShopItemSO item in items)
        {
            iconMats[item] = Unlit($"Shop_Icon_{item.name}", Color.white, item.icon != null ? item.icon.texture : null);
        }
    }

    /// <summary>
    /// The default font with fatter strokes: thin dark letters on light paper wash out at arm's
    /// length (and in the headset), so printed text gets a dilated face.
    /// </summary>
    private static Material InkTextMaterial()
    {
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        string path = $"{MaterialDir}/{font.name} Ink.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (m == null)
        {
            m = new Material(font.material);
            AssetDatabase.CreateAsset(m, path);
        }

        m.shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
        m.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
        m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.28f);
        ShaderUtilities.UpdateShaderRatios(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static TextMeshPro InkText(string name, Transform parent, Vector3 position, string text, float size, Vector2 box,
        FontStyles style = FontStyles.Normal)
    {
        // The project renders in linear space and TextMeshPro passes vertex colours through as they
        // are, so a dark sRGB brown would show up washed out: give it the linear value instead.
        TextMeshPro tmp = DeadlockSceneKit.Text(name, parent, position, text, size, Ink.linear, box, style);
        tmp.fontSharedMaterial = inkText;
        return tmp;
    }

    private static void TabMaterials(ShopCategory category, System.Func<Vector2, float> glyph)
    {
        Texture2D tex = DeadlockShopIcons.Tab($"Tab_{category}", category, glyph);
        tabMats[category] = new[]
        {
            Unlit($"Shop_Tab_{category}_Open", Color.white, tex),
            Unlit($"Shop_Tab_{category}_Closed", new Color(0.5f, 0.47f, 0.44f), tex),
        };
    }

    private static Material Unlit(string name, Color color, Texture texture = null, bool transparent = false)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }

        m.shader = shader;
        m.SetColor("_BaseColor", color);
        m.SetTexture("_BaseMap", texture);

        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = -1;
        }

        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ board

    private static void BuildBoard(string name, Faction side, Vector3 position, Vector3 lookFrom, ShopItemSO[] items)
    {
        var root = new GameObject(name);
        Vector3 away = position - lookFrom;
        away.y = 0f;
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(away));
        Transform t = root.transform;

        int ground = LayerMask.NameToLayer(DeadlockSetupTools.GroundLayer);
        int soulLayer = LayerMask.NameToLayer(DeadlockSetupTools.SoulLayer);

        // Frame on two posts; the paper on its face.
        GameObject frame = DeadlockSceneKit.Box("Frame", t, new Vector3(0f, PaperCenterY, 0.04f), new Vector3(PaperWidth + 0.16f, PaperHeight + 0.16f, 0.08f), wood);
        frame.layer = ground;

        foreach (float x in new[] { -1.15f, 1.15f })
        {
            GameObject post = DeadlockSceneKit.Box("Post", t, new Vector3(x, (PaperCenterY - PaperHeight / 2f) / 2f, 0.04f), new Vector3(0.1f, PaperCenterY - PaperHeight / 2f, 0.1f), wood);
            post.layer = ground;
        }

        Quad("Paper", t, new Vector3(0f, PaperCenterY, -0.004f), new Vector2(PaperWidth, PaperHeight), paper);
        Trim(t, new Vector3(0f, PaperCenterY, -0.006f), new Vector2(PaperWidth - 0.04f, PaperHeight - 0.04f), 0.012f, gold);
        Trim(t, new Vector3(0f, PaperCenterY, -0.006f), new Vector2(PaperWidth - 0.1f, PaperHeight - 0.1f), 0.005f, ink);
        Corners(t, new Vector3(0f, PaperCenterY, -0.007f), new Vector2(PaperWidth - 0.04f, PaperHeight - 0.04f));

        ShopBoard board = root.AddComponent<ShopBoard>();
        AudioSource audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.maxDistance = 20f;

        Header(t);
        TMP_Text souls = SoulsCounter(t);

        // Tabs for the categories that have something on sale, top to bottom.
        ShopCategory[] categories = items.Select(i => i.category).Distinct().OrderBy(c => (int)c).ToArray();
        var pagesRoot = new GameObject("Pages").transform;
        pagesRoot.SetParent(t, false);

        var pages = new GameObject[categories.Length];
        var tabs = new ShopTab[categories.Length];
        var cards = new ShopCard[items.Length];

        for (int c = 0; c < categories.Length; c++)
        {
            tabs[c] = Tab(t, board, c, categories[c], new Vector3(-PaperWidth / 2f - 0.16f, 2.22f - c * 0.32f, -0.012f), soulLayer);

            var page = new GameObject($"Page_{categories[c]}");
            page.transform.SetParent(pagesRoot, false);
            pages[c] = page;

            // The tab's tiers share the left column, cheapest at the top, so a tab with fewer
            // tiers gets taller blocks instead of empty space.
            int[] tiers = items.Where(i => i.category == categories[c]).Select(i => i.tier).Distinct().OrderBy(x => x).ToArray();
            float rowHeight = (ContentTop - ContentBottom - (tiers.Length - 1) * TierGap) / tiers.Length;

            for (int slot = 0; slot < tiers.Length; slot++)
            {
                int tier = tiers[slot];
                float top = ContentTop - slot * (rowHeight + TierGap);
                var rect = new Rect(TiersLeft, top - rowHeight, TiersRight - TiersLeft, rowHeight);
                List<int> inTier = Enumerable.Range(0, items.Length).Where(i => items[i].category == categories[c] && items[i].tier == tier).ToList();
                Block(page.transform, board, rect, tier, inTier, items, cards, soulLayer);
            }
        }

        Renderer detailIcon = DetailPanel(t, out TMP_Text title, out TMP_Text body, out TMP_Text hint);
        GameObject sealedOverlay = Sealed(t);

        var heroStand = new GameObject("HeroStand");
        heroStand.transform.SetParent(t, false);
        heroStand.transform.localPosition = new Vector3(-PaperWidth / 2f - 0.75f, 0f, -0.35f);
        heroStand.transform.localRotation = Quaternion.Euler(0f, 160f, 0f);
        ShopHeroStand stand = heroStand.AddComponent<ShopHeroStand>();
        var standSo = new SerializedObject(stand);
        standSo.FindProperty("board").objectReferenceValue = board;
        standSo.FindProperty("fallbackHero").objectReferenceValue = AssetDatabase.LoadAssetAtPath<HeroDefinitionSO>(SevenPath);
        standSo.ApplyModifiedPropertiesWithoutUndo();

        DeadlockSceneKit.PointLight("Light", t, new Vector3(0f, 2.4f, -1.4f), new Color(1f, 0.85f, 0.65f), 1.4f, 4.5f);

        var so = new SerializedObject(board);
        so.FindProperty("faction").enumValueIndex = (int)side;
        SetArray(so.FindProperty("items"), items);
        SetArray(so.FindProperty("cards"), cards);
        SetArray(so.FindProperty("pages"), pages);
        SetArray(so.FindProperty("tabs"), tabs);
        so.FindProperty("soulsLabel").objectReferenceValue = souls;
        so.FindProperty("detailTitle").objectReferenceValue = title;
        so.FindProperty("detailBody").objectReferenceValue = body;
        so.FindProperty("detailHint").objectReferenceValue = hint;
        so.FindProperty("detailIcon").objectReferenceValue = detailIcon;
        SetArray(so.FindProperty("iconMaterials"), items.Select(i => (Object)iconMats[i]).ToArray());
        so.FindProperty("sealedOverlay").objectReferenceValue = sealedOverlay;
        so.FindProperty("audioSource").objectReferenceValue = audio;
        so.FindProperty("buyClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(BuyClipPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Only the first page shows until the board opens a tab at runtime.
        for (int i = 1; i < pages.Length; i++)
        {
            pages[i].SetActive(false);
        }
    }

    /// <summary>The name plaque in the top left: a dark board with a gold rim, like a shop sign.</summary>
    private static void Header(Transform t)
    {
        Vector3 c = new Vector3(-0.62f, 2.32f, -0.008f);
        Quad("HeaderBack", t, c, new Vector2(1.18f, 0.2f), dark);
        Trim(t, c + new Vector3(0f, 0f, -0.002f), new Vector2(1.14f, 0.16f), 0.006f, gold);

        TextMeshPro title = DeadlockSceneKit.Text("Title", t, c + new Vector3(0f, 0.025f, -0.006f), "BAZAR DEL RITUAL", 0.82f, Gold,
            new Vector2(1.1f, 0.11f), FontStyles.Bold);
        title.characterSpacing = 6f;
        title.enableWordWrapping = false;
        DeadlockMenuPolish.Glow(title);

        TextMeshPro sub = DeadlockSceneKit.Text("Subtitle", t, c + new Vector3(0f, -0.058f, -0.006f), "PODER A CAMBIO DE ALMAS", 0.34f, Cream,
            new Vector2(1.1f, 0.045f));
        sub.characterSpacing = 14f;
        sub.enableWordWrapping = false;
        DeadlockMenuPolish.Glow(sub);
    }

    private static TMP_Text SoulsCounter(Transform t)
    {
        Vector3 c = new Vector3(0.95f, 2.32f, -0.01f);
        TextMeshPro label = InkText("SoulsLabel", t, c + new Vector3(-0.5f, 0f, -0.004f), "TUS ALMAS", 0.58f,
            new Vector2(0.45f, 0.08f), FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Right;
        label.enableWordWrapping = false;

        Quad("SoulsTag", t, c, new Vector2(0.5f, 0.125f), tagMat);
        TextMeshPro number = DeadlockSceneKit.Text("Souls", t, c + new Vector3(0.07f, 0f, -0.004f), "0", 0.85f, Gold,
            new Vector2(0.32f, 0.11f), FontStyles.Bold);
        DeadlockMenuPolish.Glow(number);
        return number;
    }

    private static ShopTab Tab(Transform t, ShopBoard board, int index, ShopCategory category, Vector3 position, int layer)
    {
        var go = new GameObject($"Tab_{category}");
        go.transform.SetParent(t, false);
        go.transform.localPosition = position;
        go.layer = layer;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.24f, 0.28f, 0.04f);

        GameObject open = Quad("Open", go.transform, Vector3.zero, new Vector2(0.24f, 0.28f), tabMats[category][0]);
        GameObject closed = Quad("Closed", go.transform, Vector3.zero, new Vector2(0.24f, 0.28f), tabMats[category][1]);
        closed.SetActive(false);

        ShopTab tab = go.AddComponent<ShopTab>();
        var so = new SerializedObject(tab);
        so.FindProperty("board").objectReferenceValue = board;
        so.FindProperty("tabIndex").intValue = index;
        so.FindProperty("openLook").objectReferenceValue = open;
        so.FindProperty("closedLook").objectReferenceValue = closed;
        so.ApplyModifiedPropertiesWithoutUndo();
        return tab;
    }

    /// <summary>
    /// One tier of a tab: a slightly darker panel across the left column with the tier's price on a
    /// tag over its top edge. A tier with a single item shows its name and effect beside the card,
    /// so the block reads at a glance from a couple of metres.
    /// </summary>
    private static void Block(Transform page, ShopBoard board, Rect rect, int tier, List<int> itemIndices,
        ShopItemSO[] items, ShopCard[] cards, int layer)
    {
        var go = new GameObject($"Tier_{tier}");
        go.transform.SetParent(page, false);
        go.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0f);
        Transform b = go.transform;

        Quad("Back", b, new Vector3(0f, 0f, -0.008f), rect.size, block);
        Trim(b, new Vector3(0f, 0f, -0.01f), rect.size, 0.006f, ink);

        float left = -rect.width / 2f, right = rect.width / 2f, top = rect.height / 2f;
        int price = itemIndices.Min(i => items[i].cost);

        Vector3 tagPos = new Vector3(left + 0.2f, top, -0.014f);
        Quad("PriceTag", b, tagPos, new Vector2(0.34f, 0.085f), tagMat);
        TextMeshPro priceText = DeadlockSceneKit.Text("Price", b, tagPos + new Vector3(0.05f, 0f, -0.004f), price.ToString(), 0.6f, Gold,
            new Vector2(0.22f, 0.08f), FontStyles.Bold);
        DeadlockMenuPolish.Glow(priceText);

        // Just above the block's top edge, beside the tag, so the border line doesn't strike it through.
        TextMeshPro tierText = InkText("Tier", b, new Vector3(left + 0.66f, top + 0.03f, -0.014f), $"NIVEL {tier}", 0.42f,
            new Vector2(0.5f, 0.06f), FontStyles.Bold);
        tierText.alignment = TextAlignmentOptions.Left;
        tierText.characterSpacing = 10f;

        float cardHeight = Mathf.Min(rect.height - 0.09f, 0.42f);
        float cardWidth = cardHeight * 0.72f;
        float cardY = -0.025f;

        for (int k = 0; k < itemIndices.Count; k++)
        {
            int index = itemIndices[k];
            Vector3 cardPos = new Vector3(left + 0.07f + cardWidth / 2f + k * (cardWidth + 0.05f), cardY, 0f);
            cards[index] = Card(b, board, index, items[index], cardPos, new Vector2(cardWidth, cardHeight), layer);
        }

        if (itemIndices.Count == 1)
        {
            ShopItemSO item = items[itemIndices[0]];
            float textLeft = left + 0.07f + cardWidth + 0.08f;
            float width = right - 0.05f - textLeft;
            string max = item.maxStacks > 0 ? $"\n<size=75%>Máximo {item.maxStacks}</size>" : "";
            TextMeshPro desc = InkText("Description", b, new Vector3(textLeft + width / 2f, cardY, -0.014f),
                $"<b>{item.itemName}</b>\n<size=82%><i>{item.description}</i></size>{max}", 0.62f, new Vector2(width, cardHeight));
            desc.alignment = TextAlignmentOptions.Left;
            desc.lineSpacing = -8f;
        }
    }

    /// <summary>
    /// An item card: its icon on cream card stock, its name under it when the card is big enough to
    /// read it, and a pip per stack along the bottom.
    /// </summary>
    private static ShopCard Card(Transform parent, ShopBoard board, int index, ShopItemSO item, Vector3 position, Vector2 size, int layer)
    {
        float w = size.x, h = size.y;
        bool withName = h >= 0.3f;
        var go = new GameObject($"Card_{item.name}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.layer = layer;
        Transform c = go.transform;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(w, h, 0.03f);
        box.center = new Vector3(0f, 0f, -0.015f);

        Quad("Back", c, new Vector3(0f, 0f, -0.014f), size, card);
        Trim(c, new Vector3(0f, 0f, -0.015f), size, 0.005f, ink);

        float icon = Mathf.Min(w - 0.03f, h - (withName ? 0.12f : 0.055f));
        float iconY = h / 2f - 0.015f - icon / 2f;
        Quad("Icon", c, new Vector3(0f, iconY, -0.017f), new Vector2(icon, icon), iconMats[item]);

        TextMeshPro label = null;

        if (withName)
        {
            label = InkText("Name", c, new Vector3(0f, iconY - icon / 2f - 0.034f, -0.017f), item.itemName, 0.3f,
                new Vector2(w - 0.02f, 0.06f), FontStyles.Bold);
            label.lineSpacing = -25f;
        }

        // Pips along the bottom edge: an empty socket per stack, filled as they are bought.
        int stacks = Mathf.Max(1, item.maxStacks);
        var pips = new GameObject[stacks];
        float pipY = -h / 2f + 0.02f;

        for (int i = 0; i < stacks; i++)
        {
            float x = (i - (stacks - 1) / 2f) * 0.035f;
            Quad("PipSocket", c, new Vector3(x, pipY, -0.017f), new Vector2(0.024f, 0.024f), ink);
            pips[i] = Quad("Pip", c, new Vector3(x, pipY, -0.018f), new Vector2(0.016f, 0.016f), gold);
            pips[i].SetActive(false);
        }

        GameObject dim = Quad("Dimmed", c, new Vector3(0f, 0f, -0.02f), size, dimMat);

        var selected = new GameObject("Selected");
        selected.transform.SetParent(c, false);
        Trim(selected.transform, new Vector3(0f, 0f, -0.022f), size + new Vector2(0.03f, 0.03f), 0.014f, selectedMat);
        selected.SetActive(false);

        var maxed = new GameObject("Maxed");
        maxed.transform.SetParent(c, false);
        Trim(maxed.transform, new Vector3(0f, 0f, -0.021f), size + new Vector2(0.012f, 0.012f), 0.01f, gold);
        maxed.SetActive(false);

        ShopCard shopCard = go.AddComponent<ShopCard>();
        var so = new SerializedObject(shopCard);
        so.FindProperty("board").objectReferenceValue = board;
        so.FindProperty("itemIndex").intValue = index;
        so.FindProperty("nameLabel").objectReferenceValue = label;
        SetArray(so.FindProperty("pips"), pips);
        so.FindProperty("dimmed").objectReferenceValue = dim;
        so.FindProperty("selectedFrame").objectReferenceValue = selected;
        so.FindProperty("maxedFrame").objectReferenceValue = maxed;
        so.ApplyModifiedPropertiesWithoutUndo();
        return shopCard;
    }

    /// <summary>
    /// The chosen item on a dark panel down the right column: name, a big icon, what it does, its
    /// price and what to do next. Returns the icon, which the board swaps per item.
    /// </summary>
    private static Renderer DetailPanel(Transform t, out TMP_Text title, out TMP_Text body, out TMP_Text hint)
    {
        var size = new Vector2(DetailRight - DetailLeft, ContentTop - ContentBottom);
        var go = new GameObject("Detail");
        go.transform.SetParent(t, false);
        go.transform.localPosition = new Vector3((DetailLeft + DetailRight) / 2f, (ContentTop + ContentBottom) / 2f, 0f);
        Transform d = go.transform;

        Quad("Back", d, new Vector3(0f, 0f, -0.008f), size, dark);
        Trim(d, new Vector3(0f, 0f, -0.01f), size - new Vector2(0.03f, 0.03f), 0.006f, gold);

        TextMeshPro titleText = DeadlockSceneKit.Text("Title", d, new Vector3(0f, 0.46f, -0.014f), "ELIGE UN OBJETO", 0.6f, Gold,
            new Vector2(size.x - 0.08f, 0.13f), FontStyles.Bold);
        titleText.characterSpacing = 4f;
        titleText.lineSpacing = -20f;
        DeadlockMenuPolish.Glow(titleText);

        GameObject icon = Quad("Icon", d, new Vector3(0f, 0.21f, -0.014f), new Vector2(0.3f, 0.3f), dark);
        icon.SetActive(false);

        TextMeshPro bodyText = DeadlockSceneKit.Text("Body", d, new Vector3(0f, -0.14f, -0.014f), "", 0.5f, Cream,
            new Vector2(size.x - 0.08f, 0.36f));
        DeadlockMenuPolish.Glow(bodyText);

        TextMeshPro hintText = DeadlockSceneKit.Text("Hint", d, new Vector3(0f, -0.46f, -0.014f), "", 0.5f, new Color(0.55f, 1f, 0.7f),
            new Vector2(size.x - 0.06f, 0.13f), FontStyles.Bold);
        DeadlockMenuPolish.Glow(hintText);

        title = titleText;
        body = bodyText;
        hint = hintText;
        return icon.GetComponent<Renderer>();
    }

    /// <summary>Veil over the rival's board: it can be seen, not used.</summary>
    private static GameObject Sealed(Transform t)
    {
        var go = new GameObject("Sealed");
        go.transform.SetParent(t, false);
        Quad("Veil", go.transform, new Vector3(0f, 1.6f, -0.03f), new Vector2(PaperWidth - 0.12f, 1.2f), sealedMat);
        TextMeshPro text = DeadlockSceneKit.Text("Text", go.transform, new Vector3(0f, 1.62f, -0.034f), "TIENDA DEL RIVAL", 1.3f, Gold,
            new Vector2(2.2f, 0.2f), FontStyles.Bold);
        text.characterSpacing = 10f;
        DeadlockMenuPolish.Glow(text);
        go.SetActive(false);
        return go;
    }

    // ------------------------------------------------------------------ pieces

    private static GameObject Quad(string name, Transform parent, Vector3 position, Vector2 size, Material material)
    {
        GameObject quad = DeadlockSceneKit.Primitive(PrimitiveType.Quad, name, parent, position, new Vector3(size.x, size.y, 1f), material);
        Renderer r = quad.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return quad;
    }

    /// <summary>A rectangular outline of four thin quads.</summary>
    private static void Trim(Transform parent, Vector3 center, Vector2 size, float width, Material material)
    {
        Quad("TrimTop", parent, center + new Vector3(0f, size.y / 2f - width / 2f, 0f), new Vector2(size.x, width), material);
        Quad("TrimBottom", parent, center + new Vector3(0f, -size.y / 2f + width / 2f, 0f), new Vector2(size.x, width), material);
        Quad("TrimLeft", parent, center + new Vector3(-size.x / 2f + width / 2f, 0f, 0f), new Vector2(width, size.y), material);
        Quad("TrimRight", parent, center + new Vector3(size.x / 2f - width / 2f, 0f, 0f), new Vector2(width, size.y), material);
    }

    /// <summary>Art-deco corners: a gold diamond with two stepped bars in each corner of the paper.</summary>
    private static void Corners(Transform parent, Vector3 center, Vector2 size)
    {
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = center + new Vector3(sx * (size.x / 2f - 0.05f), sy * (size.y / 2f - 0.05f), -0.001f);
                GameObject diamond = Quad("CornerDiamond", parent, corner, new Vector2(0.05f, 0.05f), gold);
                diamond.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                Quad("CornerBarH", parent, corner + new Vector3(-sx * 0.09f, 0f, 0f), new Vector2(0.1f, 0.008f), gold);
                Quad("CornerBarV", parent, corner + new Vector3(0f, -sy * 0.09f, 0f), new Vector2(0.008f, 0.1f), gold);
            }
        }
    }

    private static void SetArray(SerializedProperty property, Object[] values)
    {
        property.arraySize = values.Length;

        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
