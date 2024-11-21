using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using StardewValley.Objects;
using static StardewValley.Minigames.TargetGame;
using xTile.Tiles;
using GenericModConfigMenu;


namespace AutoSeedAndFertilizer
{
    public sealed class ModConfig
    {
        public bool isConsumable { get; set; } = true; // Determines whether to consume seeds/fertilizers or not
        public int targetScanRadius { get; set; } = 1; // Determines the distance between player and target for when auto-planting becomes triggered
        public int targetExecutionRadius { get; set; } = 1; // Determines the area around the player which covers the nearest targets to be subject for auto-planting
    }


    internal sealed class ModEntry : StardewModdingAPI.Mod
    {
        //public bool isConsumable { get; set; } = false; // Determines whether to consume seeds/fertilizers or not
        //public int targetScanRadius { get; set; } = 1; // Determines the distance between player and target for when auto-planting becomes triggered
        //public int targetExecutionRadius { get; set; } = 1; // Determines the area around the player which covers the nearest targets to be subject for auto-planting

        private bool isRightClickHeld = false; // Tracks if the right mouse button is being held
        private bool isAddMode = true; // Determines the current mode (add or delete)
        private Item? lastHeldItem;
        private readonly HashSet<Vector2> targetedTiles = new(); // Tracks targeted tiles
        private Texture2D? crosshairTexture; // Holds the crosshair texture
        private SButton keyButton = SButton.MouseRight;

        private ModConfig Config;


        public override void Entry(IModHelper helper)
        {
            //// Load the configuration
            this.Config = this.Helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonReleased += OnButtonReleased;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;

            crosshairTexture = helper.ModContent.Load<Texture2D>("assets/crosshair.png");
        }


        // MAIN FUNCTIONS
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null)
                return;

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Consumable Seeds/Fertilizers",
                tooltip: () => "Will consume seeds and fertilizers if True, as well as limit the number of crosshairs spawned to the current stack available.",
                getValue: () => this.Config.isConsumable,
                setValue: value => this.Config.isConsumable = value
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => "Detection Range",
                tooltip: () => "Dictates the minimum distance required between player and a crosshair to trigger Auto Seed and Fertilizer.",
                getValue: () => this.Config.targetScanRadius,
                setValue: value => this.Config.targetScanRadius = value,
                min: 1,
                max: 20,
                interval: 1
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => "Execution Range",
                tooltip: () => "Dictates the distance around the player which covers the crosshairs to execute Auto Seed and Fertilizer to.",
                getValue: () => this.Config.targetExecutionRadius,
                setValue: value => this.Config.targetExecutionRadius = value,
                min: 1,
                max: 20,
                interval: 1
            );
        }


        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (e.Button == keyButton)
            {
                isRightClickHeld = true;

                Vector2 currentTile = GetCursorPosition();
                InferCrosshairMode(currentTile);
            }
        }


        private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
        {
            if (e.Button == keyButton)
            {
                isRightClickHeld = false;
            }
        }


        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!isRightClickHeld)
            {
                return;
            }

            Vector2 currentTile = GetCursorPosition();
            UpdateTargetedTile(currentTile);
            ClearFilledTiles();
        }


        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (targetedTiles.Count == 0)
            {
                return;
            }

            if (!(IsHoldingSeeds() || IsHoldingFertilizer()))
            {
                targetedTiles.Clear();
                return;
            }

            ScanTerrain();
            RenderCrosshairs(e);
        }


        // HELPER FUNCTIONS
        private Vector2 GetCurrentPlayerCoordinates()
        {
            Vector2 playerCoordinates = Game1.player.Tile;
            return playerCoordinates;
        }

        private List<Vector2> GetCurrentPlayerRadius(int size)
        {
            Vector2 playerCoordinates = GetCurrentPlayerCoordinates();

            List<Vector2> radius = new List<Vector2>();
            for (int x = -size; x <= size; x++)
            {
                for (int y = -size; y <= size; y++)
                {
                    Vector2 tile = new Vector2(playerCoordinates.X + x, playerCoordinates.Y + y);
                    radius.Add(tile);
                }
            }

            return radius;
        }


        private Item GetCurrentSelectedItem()
        {
            Item? selectedItem = Game1.player.CurrentItem;

            if (selectedItem != lastHeldItem)
            {
                //Monitor.Log($"Selected item: {selectedItem.Name}, {selectedItem.QualifiedItemId}", LogLevel.Info);
                targetedTiles.Clear();
                lastHeldItem = selectedItem;
            }

            return selectedItem;
        }


        private Boolean IsHoldingSeeds()
        {
            Item selectedItem = GetCurrentSelectedItem();

            return selectedItem is StardewValley.Object && 
                selectedItem.Category == StardewValley.Object.SeedsCategory;
        }


        private Boolean IsHoldingFertilizer()
        {
            Item selectedItem = GetCurrentSelectedItem();

            return selectedItem is StardewValley.Object &&
                selectedItem.Category == StardewValley.Object.fertilizerCategory;
        }


        private Boolean IsTilledTile(Vector2 currentTile)
        {
            Game1.currentLocation.terrainFeatures.TryGetValue(currentTile, out var feature);
            return feature is HoeDirt;
        }


        private Boolean IsTargetTile(Vector2 currentTile)
        {
            return targetedTiles.Contains(currentTile);
        }


        private Boolean IsWithinReachTile(Vector2 currentTile)
        {
            List<Vector2> playerRadius = GetCurrentPlayerRadius(this.Config.targetScanRadius);

            foreach (Vector2 radiusTile in playerRadius)
            {
                if (targetedTiles.Contains(radiusTile))
                    return true;
            }
            return false;
        }


        private Boolean IsUnplantedTile(Vector2 currentTile)
        {
            return Game1.currentLocation.terrainFeatures.TryGetValue(currentTile, out var feature) && feature is HoeDirt dirt && dirt.crop == null;
        }


        private Boolean IsUnfertilziedTile(Vector2 currentTile)
        {
            return Game1.currentLocation.terrainFeatures.TryGetValue(currentTile, out var feature) && feature is HoeDirt dirt && !dirt.HasFertilizer();
        }


        private Vector2 GetCursorPosition()
        {
            return Helper.Input.GetCursorPosition().Tile;
        }


        private void InferCrosshairMode(Vector2 currentTile)
        {
            if (IsTargetTile(currentTile))
            {
                isAddMode = false;
            }
            else if (IsTilledTile(currentTile))
            {
                isAddMode = true;
            }
        }


        private void UpdateTargetedTile(Vector2 currentTile)
        {
            if (isAddMode)
            {
                if (this.Config.isConsumable && IsMaxedTargetedTiles())
                {
                    return;
                }
                if (!IsTargetTile(currentTile) && IsTilledTile(currentTile) && IsUnplantedTile(currentTile) && IsHoldingSeeds())
                {
                    targetedTiles.Add(currentTile);
                }
                if (!IsTargetTile(currentTile) && IsTilledTile(currentTile) && IsUnfertilziedTile(currentTile) && IsHoldingFertilizer())
                {
                    targetedTiles.Add(currentTile);
                }
            }
            else if (!isAddMode)
            {
                if (IsTargetTile(currentTile))
                {
                    targetedTiles.Remove(currentTile);
                }
            }
        }


        private bool IsMaxedTargetedTiles()
        {
            Item? selectedItem = GetCurrentSelectedItem();

            if (selectedItem != null)
            {
                //Monitor.Log($"Selected item stack: {selectedItem.Stack}. Targeted tiles count {targetedTiles.Count}", LogLevel.Info);
                return targetedTiles.Count >= selectedItem.Stack;
            }
            else
            {
                return targetedTiles.Count == 0;
            }
        }


        private void RenderCrosshairs(RenderedWorldEventArgs e)
        {
            foreach (var tile in targetedTiles)
            {
                Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
                e.SpriteBatch.Draw(crosshairTexture, screenPos, null, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
            }
        }


        private void PlaceToTile(Item selectedItem, Vector2 tile, bool isFertilizer)
        {
            var farm = Game1.getFarm();
            var hoeDirt = farm.terrainFeatures[tile] as HoeDirt;

            hoeDirt?.plant(selectedItem.ItemId, Game1.player, isFertilizer);

            targetedTiles.Remove(tile);

            if (!this.Config.isConsumable)
            {
                return;
            }

            if (selectedItem != null)
            {
                if (selectedItem.Stack == 1)
                {
                    Game1.player.removeItemFromInventory(selectedItem);
                }
                selectedItem.ConsumeStack(1);
            }
        }


        private void ScanTerrain()
        {
            if (!(IsHoldingSeeds() || IsHoldingFertilizer()))
            {
                return;
            }

            if (!Game1.player.isMoving())
            {
                return;
            }

            Vector2 playerTile = GetCurrentPlayerCoordinates();
            if (!IsWithinReachTile(playerTile))
            {
                return;
            }

            Item selectedItem = GetCurrentSelectedItem();
            List<Vector2> playerRadius = GetCurrentPlayerRadius(this.Config.targetExecutionRadius);
            foreach (var currtile in playerRadius)
            {
                if (IsTilledTile(currtile) && IsTargetTile(currtile) && IsUnplantedTile(currtile) && IsHoldingSeeds())
                {
                    PlaceToTile(selectedItem, currtile, false);
                }
                if (IsTilledTile(currtile) && IsTargetTile(currtile) && IsUnfertilziedTile(currtile) && IsHoldingFertilizer())
                {
                    PlaceToTile(selectedItem, currtile, true);
                }
            }
        }


        private void ClearFilledTiles()
        {
            foreach (var tile in targetedTiles)
            {
                if (!IsUnplantedTile(tile) && IsHoldingSeeds())
                {
                    targetedTiles.Remove(tile);
                }
                if (!IsUnfertilziedTile(tile) && IsHoldingFertilizer())
                {
                    targetedTiles.Remove(tile);
                }
            }
        }
    }
}