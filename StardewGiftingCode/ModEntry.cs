using GenericModConfigMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.Objects;

namespace StardewGifting
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private ISet<string> npcsTalkedTo = new HashSet<string>();

        private bool showNpcResponse = true;

        private float configuredSantaMultiplier = 0.8f;

        private float friendshipMultiplier = 1.0f;

        private IDictionary<string, NPC>? npcs;

        // I'm unsure how expensive character data to NPC lookup is but may as well cache it.
        private IDictionary<string, NPC> NPCs { 
            get
            {
                if(npcs == null )
                {
                    npcs = new Dictionary<string, NPC>();
                    foreach (KeyValuePair<string, CharacterData> character in Game1.characterData)
                    {
                        NPC npc = Game1.getCharacterFromName(character.Key);
                        if (npc != null)
                        {
                            npcs[character.Key] = npc;
                        }
                    }
                }
                return npcs;
            } 
        }



        private enum GiftUniversality
        {
            Unknown,
            NotUniversal,
            UniversalLike,
            UniversalLove
            // Universal neutral/dislike/hate are essentially irrelevant to us.
        }

        // Lazily calculates whether a particular gift is "universally" liked/loved by NPCs, so we can determine if the gift is "personalized". i.e. liked more by them specifically.
        private IDictionary<string, GiftUniversality> universalGiftLevel = new Dictionary<string, GiftUniversality>();

        private ModConfig config;

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // get Generic Mod Config Menu's API (if it's installed)
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.config)
            );

            // add some config options
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Gift After Talking To Villager",
                tooltip: () => "If true, a gift will be delivered to the NPC after talking to them, without the need of the Easy Button.",
                getValue: () => this.config.GiftAfterTalkingToVillager,
                setValue: value => this.config.GiftAfterTalkingToVillager = value
            );
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Show Gifting Notifications",
                tooltip: () => "Will display an unobtrusive popup for each gift sent along with the resultant friendship gain.",
                getValue: () => this.config.ShowGiftingNoticiations,
                setValue: value => this.config.ShowGiftingNoticiations = value
            );
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Gift All At End Of Day",
                tooltip: () => "If true, mass delivery will occur at end of day without needing to press the Very Easy button. Useful if you forget.",
                getValue: () => this.config.GiftAllAtEndOfDay,
                setValue: value => this.config.GiftAllAtEndOfDay = value
            );
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Loved Items Only",
                tooltip: () => "Only send loved gifts, not also liked.",
                getValue: () => this.config.LovedItemsOnly,
                setValue: value => this.config.LovedItemsOnly = value
            );
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Only Personalized Gifts",
                tooltip: () => "Will only send gifts that the NPC both at least likes and their preference is abnormally high. (i.e. if a gift is universally loved, it is invalid and if it is universally liked, this npc must love it).",
                getValue: () => this.config.OnlyPersonalizedGifts,
                setValue: value => this.config.OnlyPersonalizedGifts = value
            );
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Easy Early Friendship",
                tooltip: () => "If true, LovedItemsOnly and OnlyPersonalizedGifts will be ignored prior to 6 hearts. So, you can still cruise your way to 6 stars with NPCs you are less interested in.",
                getValue: () => this.config.EasyEarlyFriendship,
                setValue: value => this.config.EasyEarlyFriendship = value
            );
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Bulk Delivery Penalty",
                tooltip: () => "If true, bulk delivery (Very Easy Button or End Of Day) will only generate 80% of normal friendship gain. Individual delivery via the Easy Button will still give full credit.",
                getValue: () => this.config.BulkDeliveryPenalty,
                setValue: value => this.config.BulkDeliveryPenalty = value
            );
            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Customized Chest Color",
                tooltip: () => "Only chests of this color will be used for gifting. Setting to All will use all chests, including the default brown ones. Setting to AllNonDefault will use all chests except the default brown ones. Setting to AllNonDefaultInPlayerHome will use all non-default chests but only those in the player home.",
                getValue: () => this.config.CustomizedChestColor.ToString(),
                setValue: value =>
                {
                    ModConfig.ChestColors chestColor;
                    if (Enum.TryParse(value, out chestColor))
                    {
                        this.config.CustomizedChestColor = chestColor;
                    }
                },
                allowedValues: Enum.GetNames(typeof(ModConfig.ChestColors))
            );

        }

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            helper.Events.GameLoop.DayEnding += this.OnDayEnding;
            // Dialog should always conclude with a menu change. So, we can use this to try to perform a gifting after the player talks to an npc. Just find npcs that have not received a gift but have been talked to.
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.GameLoop.GameLaunched += new EventHandler<GameLaunchedEventArgs>(this.OnGameLaunched);
        }

        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!Context.IsWorldReady) return; // World not loaded

            if (e.Pressed.Contains(SButton.MouseRight))
            {
                var item = Game1.player.CurrentItem;
                if (item != null && item.Name.EndsWith("_EasyButton") )
                {
                    // Find the nearest NPC and gift to them from our santa stash.
                    // Look through NPCS that share a location with the player.
                    NPC? nearestNpc = null;
                    double nearestDistance = double.MaxValue;
                    const double MAX_GIFTING_DISTANCE = 200.0; // Maximum distance to gift an NPC, in tiles.
                    foreach (NPC npc in Game1.currentLocation.characters)
                    {
                        double distance = Vector2.Distance(npc.Position, Game1.player.Position);
                        if (distance < MAX_GIFTING_DISTANCE && distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearestNpc = npc;
                        }
                    }
                    if (nearestNpc != null)
                    {
                        this.showNpcResponse = true;
                        // Provide full credit when gifting to an individual NPC, as opposed to the bulk gifting option, to make it more rewarding and to encourage players to use this option for important NPCs.
                        this.friendshipMultiplier = 1.0f;
                        // Gift to the nearest NPC from the santa stash.
                        giftNpc(nearestNpc.GetData(), FindAvailableGifts());
                    }
                } else if(item != null && item.Name.EndsWith("_VeryEasyButton"))
                {
                    this.showNpcResponse = true;
                    beSanta();
                }
            }
        }

        private IDictionary<string, string>? reverseNameLookup = null;
        private string? getName(String displayName)
        {
            if (reverseNameLookup == null)
            {
                reverseNameLookup = Game1.characterData.Keys.ToDictionary(name => Game1.characterData[name].DisplayName, name => name);
            }
            string? value = null;
            reverseNameLookup.TryGetValue(displayName, out value);
            return value;
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if(!this.config.GiftAfterTalkingToVillager)
            {
                return;
            }
            // Check for when we transition from DialogueBox to null.
            if (e.OldMenu?.GetType().Name == "DialogueBox" && e.NewMenu == null)
            {
                this.Monitor.Log($" Menu changed from {e.OldMenu?.GetType().Name ?? "null"} to {e.NewMenu?.GetType().Name ?? "null"}.", LogLevel.Info);

                Farmer? player = Game1.player;
                IDictionary<string, CharacterData> npcs = Game1.characterData;
                ISet<string> npcsTalkedToUpdated = (from npc
                                                    in npcs.Values
                                                    where player.hasPlayerTalkedToNPC(getName(npc.DisplayName))
                                                    select getName(npc.DisplayName)
                                                     ).ToHashSet();

                ISet<string> diff = npcsTalkedToUpdated.Except(npcsTalkedTo).ToHashSet();

                if (diff.Count > 0)
                {
                    // Presumably, this should only contain one entry, notably the NPC just spoken to.
                    giftNpc(npcs[diff.FirstOrDefault() ?? ""], FindAvailableGifts());
                    this.Monitor.Log($"Just spoke with {getName(npcs[diff.FirstOrDefault() ?? ""].DisplayName)}", LogLevel.Info);
                }


                // Update the list.
                npcsTalkedTo = npcsTalkedToUpdated;
            }
        }

        private IDictionary<Item, Chest> FindAvailableGifts()
        {
            // Find chest with name "Gifts"
            IDictionary<Item, Chest> giftsAvailable = new Dictionary<Item, Chest>();
            foreach (GameLocation location in Game1.locations)
            {
                foreach (KeyValuePair<Vector2, StardewValley.Object> pair in location.objects.Pairs)
                {
                    if (pair.Value != null && pair.Value.Name == "Chest")
                    {
                        Chest? chest = pair.Value as Chest;
                        if (chest == null)
                        {
                            continue;
                        }
                        if (
                                this.config.CustomizedChestColor == ModConfig.ChestColors.All
                                || (this.config.CustomizedChestColor == ModConfig.ChestColors.AllNonDefault && !chest.playerChoiceColor.Value.Equals(ModConfig.ChestColors.Default))
                                || (this.config.CustomizedChestColor == ModConfig.ChestColors.AllNonDefaultInPlayerHome && chest.Location.Name.Contains("FarmHouse") && !chest.playerChoiceColor.Value.Equals(ModConfig.ChestColors.Default) )
                                || chest.playerChoiceColor.Value.Equals(ModConfig.ColorFromChestColorName(this.config.CustomizedChestColor))
                            )
                        {
                            this.Monitor.Log("Found chest at location " + location.Name + " with tile position " + pair.Key, LogLevel.Info);
                            // Look through the chest's contents for items thaat can be gifted to NPCs.
                            foreach (Item item in chest.Items)
                            {
                                if (item.canBeGivenAsGift())
                                {
                                    giftsAvailable.Add(item, chest);
                                }
                            }
                        }
                    }
                }
            }
            return giftsAvailable;
        }

        private GiftUniversality calculateGiftUniverality(Item gift)
        {
            GiftUniversality universality;
            if (!universalGiftLevel.TryGetValue(gift.QualifiedItemId, out universality))
            {
                int numLiked = 0;
                int numLoved = 0;
                foreach(NPC npc in this.NPCs.Values)
                {
                    // Not also checking canReceiveGifts() because I think that depends upon having been met and I want to still consider unmet npcs.
                    if(!npc.IsVillager)
                    {
                        // We only care about villagers.
                        continue;
                    }
                    int giftPreference = npc.getGiftTasteForThisItem(gift);
                    if(giftPreference == NPC.gift_taste_like)
                    {
                        numLiked++;
                    }
                    else if(giftPreference == NPC.gift_taste_love)
                    {
                        numLoved++;
                    }
                }
                double likedRatio = numLiked / this.NPCs.Count;
                double lovedRatio = numLoved / this.NPCs.Count;
                // There are a decent number of exceptions. So, let's go with 70% as the "universal" cutoff.
                if(likedRatio > .7)
                {
                    return GiftUniversality.UniversalLike;
                }
                else if(lovedRatio > .7)
                {
                    return GiftUniversality.UniversalLove;
                }
                else
                {
                    return GiftUniversality.NotUniversal;
                }
            }
            return universality;
        }

        private bool IsPersonalizedGift(NPC npc, Item gift)
        {
            // See if we have already calculated the universality of this gift. If not, calculate it now and cache it for future reference.
            GiftUniversality universality = this.calculateGiftUniverality(gift);

            int preference = npc.getGiftTasteForThisItem(gift);
            IList<int> acceptableGiftPreferences = new List<int>() { NPC.gift_taste_like, NPC.gift_taste_love };
            // If the gift isn't at least liked, it's irrelevant.
            bool isGiftAtLeastLiked = acceptableGiftPreferences.Contains(preference);
            if (isGiftAtLeastLiked)
            {
                //We consider a gift personalized if the NPC at least likes the gift and their like/loved level is greater than the universal level.
                switch (universality)
                {
                    case GiftUniversality.NotUniversal:
                    case GiftUniversality.Unknown:
                        return true;
                    // In this case, the NPC needs to love it.
                    case GiftUniversality.UniversalLike:
                        return preference == NPC.gift_taste_love;
                    case GiftUniversality.UniversalLove:
                        // It's not personalized if everyone loves the same gift.
                        return false;
                    default:
                        return true;
                }
            }
            else
            {
                return false;
            }
        }

        private bool UseEarlyFriendshipException(NPC npc)
        {
            // By default, each heart is 250 points. Alas, this will not work with mods that change this threshold.
            return this.config.EasyEarlyFriendship && Game1.player.friendshipData[npc.getName()].Points / 250 < 6;
        }

        private Item? findBestGift(NPC npc, ICollection<Item> giftsAvailable)
        {
            Item? bestGift = null;
            int bestFriendshipPoints = 0;
            // Break ties by cheapest item. ( In terms of sale price)
            // TODO: consider including factors such as current quantity available to determine the gift whose loss will be least impactful to the player.
            int bestGiftPrice = 0;
            foreach (Item gift in giftsAvailable)
            {
                // If personalized mode is disabled, this is essentially true by default and we can skip some steps.
                bool isPersonalizedGift = !this.config.OnlyPersonalizedGifts || UseEarlyFriendshipException(npc);

                if(this.config.OnlyPersonalizedGifts)
                {
                    isPersonalizedGift = IsPersonalizedGift(npc, gift);
                    if(!isPersonalizedGift)
                    {
                        continue;
                    }
                }

                int preference = npc.getGiftTasteForThisItem(gift);

                IList<int> acceptableGiftPreferences = new List<int>() { NPC.gift_taste_like, NPC.gift_taste_love };
                // Ignore gifts that are not liked or loved
                // TODO: Consider having a config option to allow gifting of neutral gifts, as they still provide a small amount of friendship points and may be the only available gifts for certain NPCs.
                if (!acceptableGiftPreferences.Contains(preference))
                {
                    continue;
                }
                // If we are only permitting loved gifts, enforce that here.
                if(preference != NPC.gift_taste_love && this.config.LovedItemsOnly && !UseEarlyFriendshipException(npc))
                {
                    continue;
                }
                int friendshipPoints = preference switch
                {
                    NPC.gift_taste_neutral => 20,
                    NPC.gift_taste_like => 45,
                    NPC.gift_taste_love => 80,
                    _ => 0
                };
                double multiplier = gift.Quality switch
                {
                    0 => 1.0,
                    1 => 1.1,
                    2 => 1.25,
                    4 => 1.5,
                    _ => 1.0
                };
                // Take quality into account.
                friendshipPoints = (int)(friendshipPoints * multiplier);
                int price = gift.sellToStorePrice();
                // Log gift characteristics and friendship points for debugging.
                this.Monitor.Log($"Evaluating gift {gift.Name} for NPC {npc.Name}. Friendship points: {friendshipPoints}, price: {price}.", LogLevel.Info);

                if (friendshipPoints > bestFriendshipPoints || (friendshipPoints == bestFriendshipPoints && price < bestGiftPrice))
                {
                    bestFriendshipPoints = friendshipPoints;
                    bestGift = gift;
                    bestGiftPrice = price;
                }
            }
            return bestGift;
        }

        private void beSanta()
        {
            // When taking the easy option of gifting to all NPCs at once, apply a multiplier to the friendship points gained to avoid making the game too easy. This is configurable to allow players to find the right balance for their playstyle.
            this.friendshipMultiplier = this.config.BulkDeliveryPenalty ? configuredSantaMultiplier : 1.0f;
            IDictionary<string, CharacterData> npcs = Game1.characterData;

            IDictionary<Item, Chest> giftsAvailable = FindAvailableGifts();
            if (giftsAvailable.Count == 0)
            {
                // Either the player doesn't have the chest or it has no valid gifts. In either case, there's nothing to do.
                return;
            }

            foreach (CharacterData npc in npcs.Values)
            {
                giftNpc(npc, giftsAvailable);
            }
        }

        private bool canReceiveGift(Farmer player, CharacterData npcData)
        {
            string? npcName = getName(npcData.DisplayName);
            if (!npcData.CanReceiveGifts)
            {
                return false;
            }
            NPC npc = Game1.getCharacterFromName(npcName, false);
            if (npc == null)
            {
                return false;
            }
            // See if they are eligible for a gift, both based upon daily and weekly limits.
            // Alas, there doesn't seem to be an easy way to let the game's logic handle this.
            Friendship friendshipData;
            player.friendshipData.TryGetValue(npcName, out friendshipData);

            if (friendshipData == null)
            {
                return false;
            }

            if (friendshipData.GiftsToday >= 1)
            {
                return false;
            }

            Boolean isBirthday = npcData.BirthDay == Game1.Date.DayOfMonth && npcData.BirthSeason == Game1.Date.Season;

            // Weekly limit is 2. However, this is overridden by birthdays. So, ignore this if it's currently this npc's birthday (Multiple gifts on birthdays will be handled by GiftsToday check)
            if (friendshipData.GiftsThisWeek > 2 && !isBirthday)
            {
                return false;
            }
            return true;
        }

        private void giftNpc(CharacterData npcData, IDictionary<Item, Chest> giftsAvailable)
        {
            Farmer? player = Game1.player;
            if (canReceiveGift(player, npcData))
            {
                string? npcName = getName(npcData.DisplayName);
                NPC npc = Game1.getCharacterFromName(npcName, false);
                // For logging, find and state current friendship points with this NPC before giving a gift.
                int currentFriendshipPoints = player.friendshipData[npcName].Points;

                //Go through available gifts and find the item that gives the most friendship points for this NPC. Then give that item as a gift.
                Item? gift = findBestGift(npc, giftsAvailable.Keys);
                if (gift != null)
                {
                    Chest container = giftsAvailable[gift];
                    npc.receiveGift(gift as StardewValley.Object, player, true, this.friendshipMultiplier, this.showNpcResponse);
                    gift.Stack--;
                    this.Monitor.Log($"Stack size reduced to {gift.Stack}", LogLevel.Info);
                    if (gift.Stack <= 0)
                    {
                        giftsAvailable.Remove(gift);
                        // In this case, remove it from the associated chest. Simply setting stack size to 0 causes weirdness.
                        container.Items.Remove(gift);

                    }
                    // Log current friendship points with this NPC after gifting, and the change in friendship points.
                    int newFriendshipPoints = player.friendshipData[npcName].Points;
                    int friendshipPointsChange = newFriendshipPoints - currentFriendshipPoints;
                    this.Monitor.Log($"Gav {gift?.Name ?? "no gift"} to {npc.Name}. Friendship points changed by {friendshipPointsChange} to {newFriendshipPoints}.", LogLevel.Info);
                    if(this.config.ShowGiftingNoticiations)
                    {
                        Game1.addHUDMessage(new HUDMessage($"Gave {gift?.Name ?? "no gift"} to {npc.Name}. Friendship points changed by {friendshipPointsChange} to {newFriendshipPoints}.", HUDMessage.newQuest_type));
                    }
                }
            }
        }

        /*********
        ** Private methods
        *********/
        /// <summary>Raised after the player presses a button on the keyboard, controller, or mouse.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            if (this.config.GiftAllAtEndOfDay)
            {
                beSanta();

            }
        }
    }
}