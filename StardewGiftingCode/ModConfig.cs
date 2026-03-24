using Microsoft.Xna.Framework;

namespace StardewGifting
{
    public class ModConfig
    {
        /**
         * If true, a gift will be delivered to the NPC after talking to them, without the need of the Easy Button.
         */
        public bool GiftAfterTalkingToVillager { get; set; } = false;

        /**
         * Will display an unobtrusive popup for each gift sent along with the resultant friendship gain.
         */
        public bool ShowGiftingNoticiations { get; set; } = true;
        /**
         * If true, mass delivery will occur at end of day without needing to press the Very Easy button. Useful if you forget.
         */
        public bool GiftAllAtEndOfDay { get; set; } = false;

        /**
         * Only send loved gifts, not also liked.
         */
        public bool LovedItemsOnly { get; set; } = false;

        /**
         * Will only send gifts that the NPC both at least likes and their preference is abnormally high. (i.e. if a gift is universally loved, it is invalid and if it is universally liked, this npc must love it)
         */
        public bool OnlyPersonalizedGifts { get; set; } = false;

        /**
         * If true, LovedItemsOnly and OnlyPersonalizedGifts will be ignored prior to 6 hearts. So, you can still cruise your way to 6 stars with NPCs you are less interested in.
         */
        public bool EasyEarlyFriendship { get; set; } = false;

        /**
         * If true, bulk delivery (Very Easy Button or End Of Day) will only generate 80% of normal friendship gain. Individual delivery via the Easy Button will still give full credit.
         */
        public bool BulkDeliveryPenalty { get; set; } = true;

        public enum ChestColors
        {
            All,
            AllNonDefault,
            AllNonDefaultInPlayerHome,
            Default,
            DarkBlue,
            LightBlue,
            Teal,
            Aqua,
            Green,
            LimeGreen,
            Yellow,
            LightOrange,
            DarkOrange,
            Red,
            Maroon,
            LightPink,
            DarkPink,
            Magenta,
            Purple,
            DarkPurple,
            DarkGrey,
            Grey,
            LightGrey,
            White
        }

        public static Color ColorFromChestColorName(ChestColors colors)
        {
            //Unfortunately, it doesn't appear that the game is using default XNA colors so these will need to be specifically manually.
            switch (colors)
            {
                case ChestColors.DarkBlue:
                    return new Color(85, 85, 255);
                case ChestColors.LightBlue:
                    return new Color(119, 191, 255);
                case ChestColors.Teal:
                    return new Color(0, 170, 170);
                case ChestColors.Aqua:
                    return new Color(0, 234, 175);
                case ChestColors.Green:
                    return new Color(0, 170, 0);
                case ChestColors.LimeGreen:
                    return new Color(159, 236, 0);
                case ChestColors.Yellow:
                    return new Color(255, 234, 18);
                case ChestColors.LightOrange:
                    return new Color(255, 167, 18);
                case ChestColors.DarkOrange:
                    return new Color(255, 105, 18);
                case ChestColors.Red:
                    return new Color(255, 0, 0);
                case ChestColors.Maroon:
                    return new Color(135, 0, 35);
                case ChestColors.LightPink:
                    return new Color(255, 173, 199);
                case ChestColors.DarkPink:
                    return new Color(255, 117, 195);
                case ChestColors.Magenta:
                    return new Color(172, 0, 198);
                case ChestColors.Purple:
                    return new Color(143, 0, 255);
                case ChestColors.DarkPurple:
                    return new Color(89, 11, 142);
                case ChestColors.DarkGrey:
                    return new Color(64, 64, 64);
                case ChestColors.Grey:
                    return new Color(100, 100, 100);
                case ChestColors.LightGrey:
                    return new Color(200, 200, 200);
                case ChestColors.White:
                    return new Color(254, 254, 254);
                case ChestColors.AllNonDefault:
                case ChestColors.AllNonDefaultInPlayerHome:
                    return new Color(7, 7, 7);
                case ChestColors.All:
                case ChestColors.Default:
                    return Color.Black;
                default:
                    return new Color(7, 7, 7);
            }
        }

        /**
         * Only chests of this color will be used for gifting. Setting to All will use all chests, including the default brown ones. Setting to AllNonDefault will use all chests except the default brown ones. Setting to AllNonDefaultInPlayerHome will use all non-default chests but only those in the player home.
         */
        public ChestColors CustomizedChestColor { get; set; } = ChestColors.Purple;
    }
}
