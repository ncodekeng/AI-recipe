namespace Recipe.Api.Services;

/// <summary>
/// What each cuisine cooks, which ingredients each dish wants, and which ingredients belong to a cuisine.
///
/// It exists because the planner could not name a dish. With a cuisine chosen it varied only across generic
/// forms -- recipe, traybake, stew, salad, soup -- so a basket of beef, taco bread and peppers with Mexican
/// selected was searched for as "Mexican beef bread pepper stew recipe". Naming the dish is what finds the
/// dish: asking for "tacos beef pepper recipe" returns tacos, because that is what the publisher wrote the
/// page for.
///
/// Expanded from 99 dishes to 196 on 25 November 2026, after a benchmark showed a model planning the search
/// beat this table on exactly the two cuisines it covered worst -- Eastern European and Swedish, where it
/// roughly doubled basket utilisation by asking about vinegret, botwinka and pyttipanna. Rather than pay a
/// model on every search for dish names, the model was asked once, offline, for the dishes each cuisine is
/// missing; the answers were reviewed by hand and the good ones are below. That captures the gain at no
/// latency and no per-search cost.
///
/// SIGNATURE INGREDIENTS are the other half of that work. A basket of nopales, plantain and gochujang used
/// to score every dish at zero, because none of those words was a known concept, so the table fell back to
/// each cuisine's most typical dish and produced "miso soup nopale plantain gochujang recipe". An ingredient
/// that belongs to a cuisine now says so, and the basket reaches that cuisine's dishes.
///
/// GUIDANCE, NOT A LIST OF PERMITTED RECIPES. Nothing here is matched against a result, nothing is rejected
/// for being absent from it, and a dish nobody listed comes back exactly as readily as one that is. A
/// cuisine missing from this table still searches: it falls back to the generic forms.
/// </summary>
public static class CuisineDishes
{
    /// <param name="Name">What to search for, as a cook or a publisher would write it.</param>
    /// <param name="Core">
    /// IngredientNormalizer concepts the dish is built on. Empty means generally applicable to the cuisine.
    /// </param>
    /// <param name="Also">Concepts that suit it without defining it. Scored at half weight.</param>
    /// <param name="Aliases">
    /// Regional or alternative names. Searched as well as the main name, because a publisher who wrote
    /// "pytt i panna" never wrote "Swedish hash".
    /// </param>
    /// <param name="Method">How it is cooked, where that distinguishes it. Informational.</param>
    /// <param name="Meals">
    /// Meal types it suits. Empty means any. Used to avoid offering a breakfast for a dinner search.
    /// </param>
    public sealed record Dish(
        string Name,
        string[]? Core = null,
        string[]? Also = null,
        string[]? Aliases = null,
        string? Method = null,
        string[]? Meals = null)
    {
        public string[] CoreIngredients => Core ?? [];

        public string[] CompatibleIngredients => Also ?? [];

        public string[] RegionalAliases => Aliases ?? [];

        public string[] MealTypes => Meals ?? [];
    }

    private static Dish D(
        string name,
        string[]? core = null,
        string[]? also = null,
        string[]? aliases = null,
        string? method = null,
        string[]? meals = null) => new(name, core, also, aliases, method, meals);

    public static readonly (string Cuisine, Dish[] Dishes)[] Table =
    [
        ("Italian", [
            D("pasta", ["pasta"], ["tomato", "parmesan"], method: "boil"),
            D("ragu", ["beef", "tomato"], ["pasta", "carrot"], ["bolognese"], "braise"),
            D("lasagne", ["pasta", "beef"], ["mozzarella", "tomato"], ["lasagna"], "bake"),
            D("risotto", ["rice"], ["mushroom", "parmesan"], method: "simmer"),
            D("parmigiana", ["aubergine", "mozzarella"], ["tomato"], ["melanzane alla parmigiana"], "bake"),
            D("carbonara", ["pasta", "egg"], ["parmesan", "pork"], method: "boil"),
            D("cacio e pepe", ["pasta", "parmesan"], ["black pepper"]),
            D("puttanesca", ["pasta", "tomato"], ["fish"], method: "simmer"),
            D("gnocchi", ["potato"], ["parmesan", "tomato"], method: "boil"),
            D("frittata", ["egg"], ["courgette", "parmesan"], method: "fry", meals: ["Breakfast", "Lunch"]),
            D("minestrone", ["bean", "carrot"], ["tomato", "pasta"], method: "simmer"),
            D("osso buco", ["beef"], ["carrot", "tomato"], method: "braise"),
            D("saltimbocca", ["pork"], ["parmesan"], method: "fry"),
            D("caprese salad", ["mozzarella", "tomato"], [], method: "raw", meals: ["Lunch", "Salad"]),
            D("pizza", ["mozzarella", "plain flour"], ["tomato"], method: "bake"),
            D("traybake", [], ["bell pepper", "courgette"], method: "roast"),
        ]),
        ("Indian", [
            D("curry", [], ["onion", "tomato"], method: "simmer"),
            D("dal", ["lentil"], ["onion", "tomato"], ["dhal", "daal"], "simmer"),
            D("tikka masala", ["chicken", "yogurt"], ["tomato"], method: "grill"),
            D("korma", ["chicken", "coconut milk"], ["onion"], method: "simmer"),
            D("rogan josh", ["lamb"], ["yogurt", "onion"], method: "braise"),
            D("biryani", ["rice", "chicken"], ["yogurt", "onion"], method: "bake"),
            D("saag", ["spinach"], ["garlic"], ["saag aloo", "palak"], "simmer"),
            D("chana masala", ["chickpea"], ["tomato", "onion"], ["chole"], "simmer"),
            D("keema", ["lamb", "beef"], ["onion"], ["kheema"], "fry"),
            D("bhaji", ["onion", "aubergine"], ["chickpea"], ["pakora"], "fry", ["Side", "Snack"]),
            D("aloo gobi", ["potato", "broccoli"], ["onion"], method: "fry"),
            D("vindaloo", ["pork"], ["potato", "onion"], method: "braise"),
            D("butter chicken", ["chicken", "tomato"], ["yogurt", "double cream"], ["murgh makhani"], "simmer"),
            D("pilau rice", ["rice"], ["onion", "carrot"], ["pulao", "pilaf"], "simmer", ["Side"]),
            D("sambar", ["lentil"], ["tomato", "carrot"], method: "simmer"),
        ]),
        ("Mexican", [
            D("tacos", ["tortilla", "beef"], ["bell pepper", "onion", "cheddar"], method: "fry"),
            D("fajitas", ["tortilla", "bell pepper"], ["beef", "chicken", "onion"], method: "fry"),
            D("quesadillas", ["tortilla", "cheese"], ["beef", "chicken", "bell pepper"], method: "fry"),
            D("enchiladas", ["tortilla", "tomato"], ["chicken", "cheddar", "bean"], method: "bake"),
            D("burritos", ["tortilla", "rice"], ["bean", "beef"], method: "fry"),
            D("tostadas", ["tortilla", "bean"], ["tomato", "cheese"], method: "fry"),
            D("nachos", ["tortilla", "cheddar"], ["bean", "beef"], method: "bake", meals: ["Snack"]),
            D("chilli", ["beef", "bean"], ["tomato", "bell pepper"], ["chili con carne"], "simmer"),
            D("picadillo", ["beef", "potato"], ["tomato", "onion"], method: "fry"),
            D("tinga", ["chicken", "tomato"], ["onion", "tortilla"], method: "simmer"),
            D("carnitas", ["pork"], ["tortilla", "lime"], method: "braise"),
            D("pozole", ["pork"], ["onion", "lime"], method: "simmer"),
            D("chilaquiles", ["tortilla", "egg"], ["tomato", "cheese"], method: "fry", meals: ["Breakfast"]),
            D("elote", [], ["cheese", "lime"], ["mexican street corn"], "grill", ["Side"]),
            D("stuffed peppers", ["bell pepper", "rice"], ["beef", "cheese"], method: "bake"),
        ]),
        ("Japanese", [
            D("donburi rice bowl", ["rice"], ["egg", "chicken", "beef"], ["donburi"], "simmer"),
            D("teriyaki", ["chicken", "salmon"], ["rice"], method: "grill"),
            D("katsu curry", ["chicken", "plain flour"], ["rice", "carrot"], ["tonkatsu"], "fry"),
            D("yakitori", ["chicken"], ["spring onion"], method: "grill"),
            D("gyoza", ["pork"], ["plain flour", "spring onion"], method: "fry", meals: ["Side", "Snack"]),
            D("okonomiyaki", ["egg"], ["pork", "plain flour"], method: "fry"),
            D("ramen", ["rice noodle", "pasta"], ["egg", "pork", "spring onion"], method: "simmer"),
            D("udon noodle soup", ["pasta"], ["spring onion", "egg"], ["udon"], "simmer"),
            D("yakisoba", ["pasta"], ["bell pepper", "pork"], method: "fry"),
            D("oyakodon", ["chicken", "egg"], ["rice", "onion"], method: "simmer"),
            D("salmon shioyaki", ["salmon"], ["lemon"], method: "grill"),
            D("agedashi tofu", [], ["spring onion"], method: "fry", meals: ["Side"]),
            D("miso soup", [], ["spring onion"], method: "simmer", meals: ["Side", "Soup"]),
            D("curry rice", ["rice"], ["carrot", "potato", "beef"], ["kare raisu"], "simmer"),
        ]),
        ("Thai", [
            D("green curry", ["coconut milk"], ["chicken", "aubergine"], method: "simmer"),
            D("red curry", ["coconut milk"], ["beef", "bell pepper"], method: "simmer"),
            D("massaman curry", ["potato", "peanut butter"], ["coconut milk", "beef"], method: "simmer"),
            D("pad thai", ["rice noodle", "shrimp"], ["egg", "peanut butter"], method: "fry"),
            D("pad see ew", ["rice noodle"], ["broccoli", "egg", "pork"], method: "fry"),
            D("tom yum soup", ["shrimp"], ["mushroom", "lime"], method: "simmer", meals: ["Soup"]),
            D("tom kha gai", ["chicken", "coconut milk"], ["mushroom", "lime"], method: "simmer"),
            D("satay", ["peanut butter", "chicken"], ["coconut milk"], method: "grill"),
            D("larb", ["pork", "lamb"], ["lime", "spring onion"], ["laab"], "fry", ["Salad"]),
            D("som tam", [], ["peanut butter", "lime", "carrot"], ["green papaya salad"], "raw", ["Salad"]),
            D("thai basil stir fry", ["bell pepper"], ["chicken", "pork"], ["pad kra pao"], "fry"),
            D("panang curry", ["coconut milk"], ["chicken", "peanut butter"], method: "simmer"),
            D("stir fry", ["bell pepper"], ["chicken", "broccoli"], method: "fry"),
        ]),
        ("Chinese", [
            D("stir fry", ["bell pepper"], ["chicken", "broccoli", "spring onion"], method: "fry"),
            D("fried rice", ["rice", "egg"], ["spring onion", "shrimp", "pork"], method: "fry"),
            D("chow mein", ["rice noodle", "pasta"], ["bell pepper", "chicken"], method: "fry"),
            D("sweet and sour", ["bell pepper", "pork"], ["tomato", "onion"], method: "fry"),
            D("kung pao", ["chicken", "peanut butter"], ["bell pepper"], ["gong bao"], "fry"),
            D("char siu", ["pork"], ["rice"], method: "roast"),
            D("mapo tofu", [], ["pork", "spring onion"], method: "simmer"),
            D("dumplings", ["pork", "plain flour"], ["spring onion", "carrot"], ["jiaozi", "potsticker"], "steam"),
            D("hot and sour soup", ["mushroom"], ["egg", "pork"], method: "simmer", meals: ["Soup"]),
            D("beef and broccoli", ["beef", "broccoli"], ["garlic"], method: "fry"),
            D("lemon chicken", ["chicken", "lemon"], ["plain flour"], method: "fry"),
            D("egg fried noodles", ["pasta", "egg"], ["spring onion"], method: "fry"),
            D("red braised pork", ["pork"], ["rice"], ["hong shao rou"], "braise"),
            D("congee", ["rice"], ["spring onion", "egg"], method: "simmer", meals: ["Breakfast"]),
        ]),
        ("French", [
            D("casserole", [], ["carrot", "onion"], method: "braise"),
            D("gratin", ["potato", "double cream"], ["cheese"], ["gratin dauphinois"], "bake", ["Side"]),
            D("ratatouille", ["courgette", "aubergine"], ["tomato", "bell pepper"], method: "stew"),
            D("coq au vin", ["chicken", "mushroom"], ["carrot", "onion"], method: "braise"),
            D("boeuf bourguignon", ["beef", "mushroom"], ["carrot", "onion"], method: "braise"),
            D("quiche", ["egg", "cheddar"], ["plain flour", "spinach"], ["quiche lorraine"], "bake", ["Lunch"]),
            D("cassoulet", ["bean", "pork"], ["carrot"], method: "braise"),
            D("tartiflette", ["potato", "cheese"], ["double cream", "onion"], method: "bake"),
            D("blanquette de veau", ["beef"], ["carrot", "mushroom", "double cream"], method: "braise"),
            D("salmon en papillote", ["salmon"], ["lemon", "courgette"], method: "bake"),
            D("croque monsieur", ["bread", "cheese"], ["egg"], method: "grill", meals: ["Lunch"]),
            D("soupe a l oignon", ["onion", "bread"], ["cheese"], ["french onion soup"], "simmer", ["Soup"]),
            D("pissaladiere", ["onion", "plain flour"], ["fish"], method: "bake"),
            D("steak frites", ["beef", "potato"], [], method: "fry"),
            D("omelette aux fines herbes", ["egg"], ["cheese"], method: "fry", meals: ["Breakfast"]),
        ]),
        ("Mediterranean", [
            D("traybake", ["bell pepper", "courgette"], ["tomato", "chicken"], method: "roast"),
            D("moussaka", ["aubergine", "lamb"], ["tomato", "potato"], method: "bake"),
            D("shakshuka", ["egg", "tomato"], ["bell pepper", "feta"], method: "simmer", meals: ["Breakfast"]),
            D("tagine", ["chickpea", "lamb"], ["carrot", "lemon"], method: "braise"),
            D("souvlaki", ["pork", "chicken"], ["lemon", "yogurt"], method: "grill"),
            D("paella", ["rice", "shrimp"], ["bell pepper", "chicken"], method: "simmer"),
            D("greek salad", ["feta", "tomato"], ["onion"], ["horiatiki"], "raw", ["Salad", "Lunch"]),
            D("spanakopita", ["spinach", "feta"], ["egg", "plain flour"], method: "bake"),
            D("gigantes plaki", ["bean", "tomato"], ["onion"], method: "bake"),
            D("tortilla espanola", ["potato", "egg"], ["onion"], ["spanish omelette"], "fry"),
            D("gazpacho", ["tomato"], ["bell pepper", "bread"], method: "raw", meals: ["Soup"]),
            D("albondigas", ["beef", "tomato"], ["bread"], ["spanish meatballs"], "simmer"),
            D("stuffed peppers", ["bell pepper", "rice"], ["feta", "lamb"], ["gemista"], "bake"),
            D("imam bayildi", ["aubergine", "tomato"], ["onion"], method: "bake"),
            D("grilled sea bass", ["fish"], ["lemon", "olive oil"], method: "grill"),
        ]),
        ("Korean", [
            D("bibimbap", ["rice", "egg"], ["spinach", "carrot", "beef"], method: "fry"),
            D("bulgogi", ["beef"], ["rice", "spring onion"], method: "grill"),
            D("japchae", ["rice noodle", "spinach"], ["carrot", "beef", "mushroom"], ["glass noodles"], "fry"),
            D("tteokbokki", [], ["spring onion", "egg"], ["rice cakes"], "simmer", ["Snack"]),
            D("kimchi jjigae", ["pork"], ["tofu", "spring onion"], ["kimchi stew"], "simmer"),
            D("doenjang jjigae", [], ["courgette", "potato"], ["soybean paste stew"], "simmer"),
            D("dakgalbi", ["chicken", "sweet potato"], ["bell pepper", "cabbage"], method: "fry"),
            D("korean fried chicken", ["chicken"], ["plain flour"], ["dakgangjeong"], "fry"),
            D("galbi jjim", ["beef"], ["carrot", "potato"], ["braised short ribs"], "braise"),
            D("kimchi fried rice", ["rice"], ["egg", "pork"], ["kimchi bokkeumbap"], "fry"),
            D("haemul pajeon", ["spring onion", "plain flour"], ["shrimp", "egg"], ["seafood pancake"], "fry"),
            D("bibim guksu", ["pasta"], ["carrot", "egg"], ["spicy cold noodles"], "boil"),
            D("samgyetang", ["chicken", "rice"], ["garlic"], ["ginseng chicken soup"], "simmer", ["Soup"]),
            D("gyeranjjim", ["egg"], ["spring onion"], ["steamed egg"], "steam", ["Side"]),
        ]),
        ("Caribbean", [
            D("jerk chicken", ["chicken"], ["spring onion", "lime"], method: "grill"),
            D("curry goat", ["lamb"], ["potato", "onion"], method: "braise"),
            D("rice and peas", ["rice", "bean"], ["coconut milk", "spring onion"], method: "simmer", meals: ["Side"]),
            D("brown stew chicken", ["chicken"], ["carrot", "bell pepper"], method: "braise"),
            D("oxtail stew", ["beef", "bean"], ["carrot"], method: "braise"),
            D("escovitch fish", ["fish"], ["carrot", "bell pepper", "onion"], method: "fry"),
            D("ackee and saltfish", ["fish"], ["onion", "bell pepper"], method: "fry", meals: ["Breakfast"]),
            D("saltfish fritters", ["fish", "plain flour"], ["spring onion"], ["stamp and go"], "fry", ["Snack"]),
            D("pepperpot", ["beef"], ["onion"], method: "braise"),
            D("callaloo", ["spinach"], ["coconut milk", "onion"], method: "simmer", meals: ["Side"]),
            D("fried plantain", ["plantain"], [], method: "fry", meals: ["Side"]),
            D("roti and curry", ["plain flour", "chickpea"], ["potato"], method: "fry"),
            D("pelau", ["rice", "chicken"], ["bean", "coconut milk"], method: "simmer"),
            D("coconut shrimp", ["shrimp", "coconut milk"], ["lime"], method: "fry"),
        ]),
        ("British", [
            D("pie", ["beef", "plain flour"], ["onion", "carrot"], ["steak pie"], "bake"),
            D("shepherds pie", ["lamb", "potato"], ["carrot", "onion"], method: "bake"),
            D("cottage pie", ["beef", "potato"], ["carrot", "onion"], method: "bake"),
            D("roast dinner", ["potato", "carrot"], ["beef", "chicken"], ["sunday roast"], "roast"),
            D("toad in the hole", ["egg", "plain flour"], ["onion"], method: "bake"),
            D("fish and chips", ["fish", "potato"], ["plain flour"], method: "fry"),
            D("bubble and squeak", ["potato"], ["carrot", "onion"], method: "fry", meals: ["Breakfast"]),
            D("stew", [], ["carrot", "onion", "beef"], ["hotpot"], "braise"),
            D("lancashire hotpot", ["lamb", "potato"], ["onion", "carrot"], method: "braise"),
            D("cauliflower cheese", ["broccoli", "cheddar"], ["double cream"], method: "bake", meals: ["Side"]),
            D("ploughmans", ["cheddar", "bread"], ["tomato"], method: "raw", meals: ["Lunch"]),
            D("cullen skink", ["fish", "potato"], ["onion"], method: "simmer", meals: ["Soup"]),
            D("liver and onions", ["onion"], ["potato"], method: "fry"),
            D("cheese and onion pasty", ["cheddar", "potato"], ["onion", "plain flour"], method: "bake", meals: ["Lunch"]),
            D("kedgeree", ["rice", "fish"], ["egg", "onion"], method: "simmer", meals: ["Breakfast"]),
            D("traybake", [], ["bell pepper", "potato"], method: "roast"),
        ]),
        ("Arabic", [
            D("kebab", ["lamb", "beef"], ["onion", "yogurt"], ["shish kebab"], "grill"),
            D("shawarma", ["chicken", "yogurt"], ["tortilla", "tomato"], method: "grill"),
            D("kofta", ["lamb", "beef"], ["onion", "tomato"], ["kafta"], "grill"),
            D("hummus", ["chickpea"], ["lemon", "garlic"], method: "blend", meals: ["Side", "Snack"]),
            D("mujadara", ["rice", "lentil"], ["onion"], method: "simmer"),
            D("fattoush", ["tomato", "bread"], ["lemon", "onion"], method: "raw", meals: ["Salad"]),
            D("tabbouleh", [], ["tomato", "lemon", "spring onion"], method: "raw", meals: ["Salad"]),
            D("maqluba", ["rice", "aubergine"], ["chicken", "tomato"], ["maqlooba"], "simmer"),
            D("shish taouk", ["chicken"], ["yogurt", "lemon"], method: "grill"),
            D("falafel", ["chickpea"], ["onion", "garlic"], method: "fry", meals: ["Lunch", "Snack"]),
            D("moutabal", ["aubergine"], ["yogurt", "lemon"], ["baba ganoush"], "grill", ["Side"]),
            D("kibbeh", ["beef", "lamb"], ["onion"], method: "fry"),
            D("molokhia", ["spinach", "chicken"], ["rice", "garlic"], method: "simmer"),
            D("warak enab", ["rice"], ["lemon", "lamb"], ["stuffed vine leaves"], "simmer", ["Side"]),
            D("lentil soup", ["lentil"], ["onion", "lemon", "carrot"], ["shorbat adas"], "simmer", ["Soup"]),
        ]),
        ("Eastern European", [
            D("borscht", ["beetroot"], ["carrot", "potato", "beef"], ["barszcz", "borsch"], "simmer", ["Soup"]),
            D("botwinka", ["beetroot"], ["potato", "egg"], ["young beet soup"], "simmer", ["Soup"]),
            D("vinegret", ["beetroot", "potato"], ["carrot", "bean"], ["vinaigrette salad", "russian beet salad"], "boil", ["Salad", "Side"]),
            D("goulash", ["beef", "bell pepper"], ["onion", "potato"], ["gulyas"], "braise"),
            D("bigos", ["pork"], ["onion", "mushroom"], ["hunters stew"], "braise"),
            D("pierogi", ["potato", "cheese"], ["onion", "plain flour"], ["varenyky"], "boil"),
            D("dumplings", ["plain flour", "potato"], ["onion"], ["kluski", "halushki"], "boil"),
            D("stuffed cabbage rolls", ["beef", "rice"], ["tomato", "onion"], ["golabki", "sarma"], "braise"),
            D("stroganoff", ["beef", "mushroom"], ["onion", "double cream"], method: "fry"),
            D("schnitzel", ["pork", "plain flour"], ["lemon", "potato"], ["kotlet schabowy"], "fry"),
            D("placki kartoflane", ["potato", "plain flour"], ["onion", "egg"], ["potato pancakes", "draniki"], "fry"),
            D("zurek", ["pork", "potato"], ["egg"], ["sour rye soup"], "simmer", ["Soup"]),
            D("solyanka", ["beef"], ["lemon", "onion"], method: "simmer", meals: ["Soup"]),
            D("olivier salad", ["potato", "egg"], ["carrot", "mayonnaise", "bean"], ["russian salad"], "boil", ["Salad", "Side"]),
            D("shchi cabbage soup", [], ["potato", "carrot", "beef"], ["shchi"], "simmer", ["Soup"]),
            D("beef stroganov", ["beef"], ["mushroom", "rice"], method: "fry"),
            D("zrazy", ["beef"], ["onion", "mushroom"], method: "braise"),
            D("kasha with mushrooms", [], ["mushroom", "onion"], ["buckwheat kasha"], "simmer", ["Side"]),
        ]),
        ("Swedish", [
            D("kottbullar", ["beef", "pork"], ["potato", "double cream"], ["swedish meatballs"], "fry"),
            D("pyttipanna", ["potato"], ["onion", "egg", "beef"], ["pytt i panna", "swedish hash"], "fry"),
            D("kalops", ["beef", "carrot"], ["onion", "potato"], ["swedish beef stew"], "braise"),
            D("gravlax", ["salmon"], ["lemon"], ["gravad lax"], "cure", ["Lunch", "Side"]),
            D("janssons frestelse", ["potato", "double cream"], ["onion", "fish"], ["janssons temptation"], "bake"),
            D("raggmunk", ["potato", "plain flour"], ["egg", "pork"], ["potato pancakes"], "fry"),
            D("arter med flask", ["bean", "pork"], ["onion"], ["pea soup"], "simmer", ["Soup"]),
            D("toast skagen", ["shrimp", "bread"], ["mayonnaise", "lemon"], method: "raw", meals: ["Lunch", "Snack"]),
            D("stekt stromming", ["fish", "potato"], ["plain flour"], ["fried herring"], "fry"),
            D("kalvfiles oscar", ["beef"], ["shrimp"], method: "fry"),
            D("biff a la lindstrom", ["beef", "beetroot"], ["onion", "egg"], method: "fry"),
            D("laxpudding", ["salmon", "potato"], ["egg", "double cream"], ["salmon pudding"], "bake"),
            D("korv stroganoff", ["double cream", "rice"], ["onion", "tomato"], ["sausage stroganoff"], "fry"),
            D("blodpudding", [], ["bean"], method: "fry", meals: ["Breakfast"]),
            D("dillkott", ["lamb"], ["carrot", "double cream"], ["lamb in dill sauce"], "braise"),
            D("flygande jakob", ["chicken", "double cream"], ["bean"], ["flying jacob"], "bake"),
        ]),
        ("Ghanaian", [
            D("jollof rice", ["rice", "tomato"], ["onion", "chicken", "bell pepper"], method: "simmer"),
            D("waakye", ["rice", "bean"], ["tomato", "egg"], method: "simmer"),
            D("groundnut soup", ["peanut butter", "chicken"], ["tomato", "onion"], ["nkatenkwan"], "simmer", ["Soup"]),
            D("light soup", ["fish", "tomato"], ["onion", "lamb"], ["nkrakra"], "simmer", ["Soup"]),
            D("palava sauce", ["spinach"], ["fish", "tomato"], ["kontomire stew"], "simmer"),
            D("red red", ["bean", "plantain"], ["tomato", "onion"], ["red red bean stew"], "simmer"),
            D("kelewele", ["plantain"], [], method: "fry", meals: ["Side", "Snack"]),
            D("banku and tilapia", ["fish"], ["tomato", "onion"], method: "grill"),
            D("fufu and soup", [], ["fish", "tomato", "lamb"], method: "pound"),
            D("kenkey", [], ["fish", "tomato"], method: "steam", meals: ["Side"]),
            D("tuo zaafi", [], ["spinach", "lamb"], method: "simmer"),
            D("shito pepper sauce", ["fish"], ["onion"], method: "fry", meals: ["Side"]),
            D("chicken kebabs", ["chicken"], ["peanut butter", "onion"], ["chichinga", "suya"], "grill"),
            D("garden egg stew", ["aubergine", "tomato"], ["fish", "onion"], method: "simmer"),
        ]),
    ];

    // ---- Ingredients that belong to a cuisine ----------------------------------------------------------
    //
    // The other half of the expansion. A basket of nopales, plantain and gochujang used to score every dish
    // at zero, because none of those words was a concept the table knew, so it fell back to each cuisine's
    // most typical dish and produced "miso soup nopale plantain gochujang recipe". An ingredient that
    // belongs to a cuisine now says so, and the basket reaches that cuisine's own dishes.
    //
    // Only ingredients that genuinely point at one kitchen are listed. Tomatoes and onions point nowhere.
    private static readonly (string Cuisine, string[] Ingredients)[] SignatureIngredients =
    [
        ("Italian", ["parmesan", "mozzarella", "pancetta", "prosciutto", "mascarpone", "ricotta",
            "arborio rice", "passata", "pesto", "balsamic vinegar", "polenta", "gnocchi", "guanciale"]),
        ("Indian", ["garam masala", "turmeric", "ghee", "paneer", "tandoori paste", "curry leaves",
            "asafoetida", "chapati flour", "mango chutney", "cardamom", "fenugreek", "urad dal"]),
        ("Mexican", ["nopales", "tortilla", "masa harina", "chipotle", "jalapeno", "tomatillo",
            "ancho chilli", "queso fresco", "refried beans", "chorizo", "cotija", "poblano"]),
        ("Japanese", ["miso paste", "mirin", "dashi", "nori", "wasabi", "panko", "sake",
            "soy sauce", "shiitake", "daikon", "edamame", "sushi rice", "katsu sauce"]),
        ("Thai", ["fish sauce", "lemongrass", "galangal", "kaffir lime", "thai basil",
            "tamarind", "palm sugar", "thai red curry paste", "thai green curry paste", "bird eye chilli"]),
        ("Chinese", ["hoisin sauce", "oyster sauce", "shaoxing wine", "five spice", "sichuan pepper",
            "black bean sauce", "water chestnut", "bok choy", "pak choi", "rice wine"]),
        ("French", ["creme fraiche", "gruyere", "dijon mustard", "herbes de provence", "puy lentils",
            "brie", "camembert", "shallots", "tarragon", "cognac", "roquefort", "comte"]),
        ("Mediterranean", ["feta", "halloumi", "tahini", "harissa", "preserved lemon", "kalamata olives",
            "orzo", "filo pastry", "ras el hanout", "manchego", "chorizo", "saffron"]),
        ("Korean", ["gochujang", "gochugaru", "kimchi", "doenjang", "sesame oil", "rice cakes",
            "perilla leaves", "sweet potato noodles", "ssamjang"]),
        ("Caribbean", ["scotch bonnet", "plantain", "jerk seasoning", "allspice", "ackee",
            "saltfish", "callaloo", "cassava", "breadfruit", "coconut milk", "oxtail"]),
        ("British", ["suet", "worcestershire sauce", "english mustard", "cheddar", "black pudding",
            "piccalilli", "bramley apple", "samphire", "stilton", "pearl barley", "mint sauce"]),
        ("Arabic", ["tahini", "sumac", "zaatar", "pomegranate molasses", "baharat", "freekeh",
            "bulgur wheat", "labneh", "halloumi", "vine leaves", "dates"]),
        ("Eastern European", ["soured cream", "dill", "caraway", "sauerkraut", "kielbasa", "beetroot",
            "buckwheat", "horseradish", "quark", "poppy seeds", "smetana", "paprika"]),
        ("Swedish", ["lingonberry", "dill", "cardamom", "pickled herring", "crispbread",
            "falukorv", "vasterbotten cheese", "allspice", "aquavit", "cloudberry"]),
        ("Ghanaian", ["plantain", "cassava", "gari", "palm oil", "egusi", "kontomire",
            "scotch bonnet", "groundnut paste", "shito", "garden eggs", "fufu flour"]),
    ];

    private static readonly Dictionary<string, List<string>> CuisinesByIngredient = BuildIngredientIndex();

    private static Dictionary<string, List<string>> BuildIngredientIndex()
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cuisine, ingredients) in SignatureIngredients)
        {
            foreach (var ingredient in ingredients)
            {
                if (!index.TryGetValue(ingredient, out var cuisines))
                {
                    index[ingredient] = cuisines = [];
                }

                cuisines.Add(cuisine);
            }
        }

        return index;
    }

    /// <summary>
    /// Cuisines an ingredient points at, most specific first. Empty where it points nowhere.
    ///
    /// Matched on whole words against the ingredient as written, so "2 tbsp gochujang paste" finds
    /// gochujang. An ingredient several cuisines share -- plantain, dill, coconut milk -- returns all of
    /// them, and the caller can take the lot.
    /// </summary>
    public static IReadOnlyList<string> CuisinesForIngredient(string? ingredient)
    {
        if (string.IsNullOrWhiteSpace(ingredient))
        {
            return [];
        }

        var padded = $" {Letters(ingredient)} ";
        return CuisinesByIngredient
            .Where(entry => padded.Contains($" {entry.Key} ", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Key.Length)
            .SelectMany(entry => entry.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Is this an ingredient the knowledge base recognises as belonging somewhere?</summary>
    public static bool IsSignatureIngredient(string? ingredient) => CuisinesForIngredient(ingredient).Count > 0;

    private static string Letters(string text) =>
        new(text.ToLowerInvariant().Select(c => char.IsLetter(c) || char.IsWhiteSpace(c) ? c : ' ').ToArray());

    /// <summary>
    /// The dishes of one cuisine, best fit for this basket first.
    ///
    /// A core ingredient the customer has scores a whole point, a compatible one half, and a dish that
    /// declares no core ingredients scores half -- below anything that matches and above anything that
    /// matches nothing -- so a basket the table says nothing specific about still gets that cuisine's
    /// everyday dishes. Ties keep table order, which is roughly how typical each dish is.
    ///
    /// A meal type the dish does not suit halves its score rather than removing it: a breakfast kedgeree is
    /// a worse answer for dinner than a shepherd's pie, not an invalid one.
    /// </summary>
    public static IReadOnlyList<Dish> For(string? cuisine, ISet<string> basketConcepts, string? mealType = null)
    {
        if (string.IsNullOrWhiteSpace(cuisine))
        {
            return [];
        }

        var dishes = Table
            .FirstOrDefault(entry => entry.Cuisine.Equals(cuisine.Trim(), StringComparison.OrdinalIgnoreCase))
            .Dishes;
        if (dishes is null || dishes.Length == 0)
        {
            return [];
        }

        // With nothing in the basket there is nothing to fit, so the table's own order decides. Scoring here
        // would put the generic dishes first and open Inspire Me on an Italian traybake instead of pasta.
        if (basketConcepts.Count == 0)
        {
            return dishes;
        }

        return dishes
            .Select((dish, index) => (dish, index, score: Score(dish, basketConcepts, mealType)))
            .OrderByDescending(entry => entry.score)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.dish)
            .ToList();
    }

    private static double Score(Dish dish, ISet<string> basketConcepts, string? mealType)
    {
        var score = dish.CoreIngredients.Length == 0
            ? 0.5d
            : dish.CoreIngredients.Count(basketConcepts.Contains);
        score += 0.5d * dish.CompatibleIngredients.Count(basketConcepts.Contains);

        if (!string.IsNullOrWhiteSpace(mealType) &&
            dish.MealTypes.Length > 0 &&
            !dish.MealTypes.Contains(mealType.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            score *= 0.5d;
        }

        return score;
    }

    /// <summary>Every cuisine the table covers, for tests and for the review export.</summary>
    public static IReadOnlyList<string> Cuisines => Table.Select(entry => entry.Cuisine).ToList();

    /// <summary>How many dishes the table holds, for the record.</summary>
    public static int DishCount => Table.Sum(entry => entry.Dishes.Length);

    /// <summary>
    /// Dish names more than one cuisine cooks. Those need their cuisine named in a query; the rest do not,
    /// because the dish already says which kitchen it came from. Derived from the table rather than listed,
    /// so adding a cuisine that also makes dumplings needs no second edit.
    /// </summary>
    private static readonly HashSet<string> SharedDishNames =
        Table
            .SelectMany(entry => entry.Dishes.Select(dish => dish.Name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsShared(string dishName) => SharedDishNames.Contains(dishName);

    /// <summary>Which cuisine a dish came from. Only meaningful for a name one cuisine alone uses.</summary>
    public static string? CuisineOf(Dish dish) =>
        Table.FirstOrDefault(entry => entry.Dishes.Contains(dish)).Cuisine;

    // What publishers actually write in recipeCuisine, mapped onto the cuisines the app offers. A publisher
    // saying "Tex-Mex" or "Sichuan" is telling us something real, and the app has no picker entry for it.
    //
    // Deliberately lossy and deliberately incomplete. Several of these group traditions that a cook would
    // rightly separate -- Greek and Spanish are not one cuisine, and Nigerian is not Ghanaian -- but the
    // app's own picker already groups them, so this maps onto what it offers rather than inventing entries
    // it has no screen for. A value that matches nothing here is discarded rather than shown.
    private static readonly (string Needle, string Cuisine)[] CuisineAliases =
    [
        ("tex-mex", "Mexican"), ("tex mex", "Mexican"), ("mexican", "Mexican"),
        ("italian", "Italian"), ("sicilian", "Italian"), ("tuscan", "Italian"),
        ("indian", "Indian"), ("punjabi", "Indian"), ("bengali", "Indian"), ("south asian", "Indian"),
        ("japanese", "Japanese"), ("thai", "Thai"),
        ("chinese", "Chinese"), ("cantonese", "Chinese"), ("sichuan", "Chinese"), ("szechuan", "Chinese"),
        ("french", "French"), ("provencal", "French"),
        ("mediterranean", "Mediterranean"), ("greek", "Mediterranean"), ("spanish", "Mediterranean"),
        ("turkish", "Mediterranean"), ("portuguese", "Mediterranean"),
        ("korean", "Korean"),
        ("caribbean", "Caribbean"), ("jamaican", "Caribbean"), ("cuban", "Caribbean"), ("trinidadian", "Caribbean"),
        ("british", "British"), ("english", "British"), ("scottish", "British"), ("welsh", "British"),
        ("irish", "British"),
        ("arabic", "Arabic"), ("lebanese", "Arabic"), ("middle eastern", "Arabic"), ("moroccan", "Arabic"),
        ("persian", "Arabic"), ("israeli", "Arabic"), ("syrian", "Arabic"),
        ("eastern european", "Eastern European"), ("polish", "Eastern European"),
        ("hungarian", "Eastern European"), ("ukrainian", "Eastern European"),
        ("russian", "Eastern European"), ("czech", "Eastern European"), ("georgian", "Eastern European"),
        ("swedish", "Swedish"), ("scandinavian", "Swedish"), ("nordic", "Swedish"), ("danish", "Swedish"),
        ("norwegian", "Swedish"), ("finnish", "Swedish"),
        ("ghanaian", "Ghanaian"), ("west african", "Ghanaian"), ("nigerian", "Ghanaian"),
    ];

    // Cuisines the app has no picker entry for but publishers legitimately name. Kept as written, because
    // "Vietnamese" is a true statement about a recipe even though nothing here maps it.
    private static readonly (string Needle, string Cuisine)[] OtherRealCuisines =
    [
        ("american", "American"), ("vietnamese", "Vietnamese"), ("malaysian", "Malaysian"),
        ("indonesian", "Indonesian"), ("filipino", "Filipino"), ("german", "German"),
        ("austrian", "Austrian"), ("swiss", "Swiss"), ("belgian", "Belgian"), ("dutch", "Dutch"),
        ("australian", "Australian"), ("cajun", "Cajun"), ("creole", "Creole"),
        ("ethiopian", "Ethiopian"), ("peruvian", "Peruvian"), ("brazilian", "Brazilian"),
        ("argentin", "Argentinian"), ("asian", "Asian"), ("african", "African"),
        ("european", "European"), ("latin", "Latin American"),
    ];

    /// <summary>
    /// A publisher's own recipeCuisine, where it actually names a cuisine.
    ///
    /// Returns null for anything that does not, which is a great deal of what the field contains: measured
    /// on 25 November 2026, live pages gave "Cheese Cuisine", "Gluten Free", "Asia" and "Asian cuisines".
    /// Those used to be passed through as the recipe's cuisine, which is worse than saying nothing.
    /// </summary>
    public static string? Canonicalise(string? publisherCuisine)
    {
        var value = publisherCuisine?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var lowered = value.ToLowerInvariant();
        foreach (var (needle, cuisine) in CuisineAliases.Concat(OtherRealCuisines))
        {
            if (lowered.Contains(needle, StringComparison.Ordinal))
            {
                return cuisine;
            }
        }

        return null;
    }

    /// <summary>
    /// The cuisine a title gives away by naming one of its dishes, or null when it does not.
    ///
    /// Only dishes one cuisine alone cooks can identify it: a page called "Chicken Stir Fry" could be Thai
    /// or Chinese, so it says nothing. Regional aliases count, because a publisher who wrote "pytt i panna"
    /// never wrote "Swedish hash". The longest match wins, so "green curry" is read before "curry".
    /// </summary>
    public static string? FromDishName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var padded = $" {Letters(title)} ";

        return Table
            .SelectMany(entry => entry.Dishes.SelectMany(dish =>
                dish.RegionalAliases.Append(dish.Name).Select(name => (entry.Cuisine, Name: name))))
            .Where(entry => !IsShared(entry.Name))
            .Where(entry => padded.Contains($" {entry.Name} ", StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Name.Length)
            .Select(entry => entry.Cuisine)
            .FirstOrDefault();
    }
}
