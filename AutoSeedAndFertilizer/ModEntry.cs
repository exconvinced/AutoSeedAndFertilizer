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
using System.Runtime.Intrinsics;


namespace AutoSeedAndFertilizer
{
    public sealed class ModConfig
    {
        public bool isConsumable { get; set; } = true; // Determines whether to consume seeds/fertilizers or not
        public bool allowSowingOutsideFarm { get; set; } = false; // Determines whether to allow sowing outside the farm or not
        public int targetScanRadius { get; set; } = 1; // Determines the distance between player and target for when auto-planting becomes triggered
        public int targetExecutionRadius { get; set; } = 1; // Determines the area around the player which covers the nearest targets to be subject for auto-planting
        public bool alwaysSowOnAnyTilledSoil { get; set; } = false; // Determines whether to always target tilled soils or not
        public bool preventFertilizerOnEmptySoil { get; set; } = true; // Determines whether to prevent fertilizing empty soils or not
        public bool useRectangularMarquee { get; set; } = true; // Determines whether to use rectangular marquee or not
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
        private Vector2? lastTile = null; // Tracks the last tile
        private Vector2? startTile = null; // Tracks the start tile
        private Texture2D? crosshairTexture; // Holds the crosshair texture
        private Texture2D? marqueeTexture; // Holds the marquee texture
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
            marqueeTexture = helper.ModContent.Load<Texture2D>("assets/marquee.png");
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
                tooltip: () => "Dictates the minimum distance required between player and a crosshair to trigger Auto Seed and Fertilizer. 0 means the trigger will happen when the player steps on a new tile.",
                getValue: () => this.Config.targetScanRadius,
                setValue: value => this.Config.targetScanRadius = value,
                min: 0,
                max: 20,
                interval: 1
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => "Execution Range",
                tooltip: () => "Dictates the distance around the player which covers the crosshairs to execute Auto Seed and Fertilizer to. 0 means the player will only sow on the tile being currently stood on.",
                getValue: () => this.Config.targetExecutionRadius,
                setValue: value => this.Config.targetExecutionRadius = value,
                min: 0,
                max: 20,
                interval: 1
            );

            configMenu.AddBoolOption(
                 mod: this.ModManifest,
                 name: () => "Always Sow on Any Tilled Soil",
                 tooltip: () => "Skip manually marking tilled soils. Instead, simply walk over the tilled soils to sow seeds/fertilizers. Useful for joystick controllers. Modify area of sowing via Execution Range.",
                 getValue: () => this.Config.alwaysSowOnAnyTilledSoil,
                 setValue: value => this.Config.alwaysSowOnAnyTilledSoil = value
             );

            configMenu.AddBoolOption(
                 mod: this.ModManifest,
                 name: () => "Prevent Fertilizing Empty Soils",
                 tooltip: () => "Always ensure that the tilled soil is already occupied by a seed/plant before allowing fertilizers to be sowed.",
                 getValue: () => this.Config.preventFertilizerOnEmptySoil,
                 setValue: value => this.Config.preventFertilizerOnEmptySoil = value
             );

            configMenu.AddBoolOption(
                 mod: this.ModManifest,
                 name: () => "Draw Rectangle To Mark Soils",
                 tooltip: () => "Instead of selecting one tile at a time, draw a rectangular area to mark tilled soils.",
                 getValue: () => this.Config.useRectangularMarquee,
                 setValue: value => this.Config.useRectangularMarquee = value
             );

            //configMenu.AddBoolOption(
            //     mod: this.ModManifest,
            //     name: () => "Allow Sowing Anywhere",
            //     tooltip: () => "Lets you sow seeds/fertilizers anywhere with tilled soils.",
            //     getValue: () => this.Config.allowSowingOutsideFarm,
            //     setValue: value => this.Config.allowSowingOutsideFarm = value
            // );
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
                startTile = null;
                lastTile = null;
                return;
            }
 
            Vector2 currentTile = GetCursorPosition();

            if (this.Config.useRectangularMarquee)
            {
                targetedTiles.Clear();
                if (startTile == null)
                {
                    startTile = currentTile;
                }
                if (lastTile != currentTile)
                {
                    lastTile = currentTile;
                }
                if (startTile != null && lastTile != null)
                {
                    UpdateTargetedTilesViaMarquee(startTile.Value, lastTile.Value);
                }
            }
            else
            {
                UpdateTargetedTile(currentTile);
            }
            ClearFilledTiles();
        }


        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (!(IsHoldingSeeds() || IsHoldingFertilizer()))
            {
                targetedTiles.Clear();
                return;
            }

            RenderRectangularMarquee(e);

            if (targetedTiles.Count == 0)
            {
                if (!this.Config.alwaysSowOnAnyTilledSoil)
                {
                    return;
                }
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
            if (this.Config.useRectangularMarquee)
            {
                isAddMode = true;
            }
            else if (IsTargetTile(currentTile))
            {
                isAddMode = false;
            }
            else if (IsTilledTile(currentTile))
            {
                isAddMode = true;
            }
        }

        private void UpdateTargetedTilesViaMarquee(Vector2 startTile, Vector2 endTile)
        {
            int startX = (int)startTile.X;
            int startY = (int)startTile.Y;
            int endX = (int)endTile.X;
            int endY = (int)endTile.Y;

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    Vector2 tile = new Vector2(x, y);
                    UpdateTargetedTile(tile);
                }
            }

            for (int y = startY; y >= endY; y--)
            {
                for (int x = startX; x >= endX; x--)
                {
                    Vector2 tile = new Vector2(x, y);
                    UpdateTargetedTile(tile);
                }
            }

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x >= endX; x--)
                {
                    Vector2 tile = new Vector2(x, y);
                    UpdateTargetedTile(tile);
                }
            }

            for (int y = startY; y >= endY; y--)
            {
                for (int x = startX; x <= endX; x++)
                {
                    Vector2 tile = new Vector2(x, y);
                    UpdateTargetedTile(tile);
                }
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
                    if (this.Config.preventFertilizerOnEmptySoil && !IsUnplantedTile(currentTile))
                    {
                        targetedTiles.Add(currentTile); 
                    }
                    else if (!this.Config.preventFertilizerOnEmptySoil)
                    {
                        targetedTiles.Add(currentTile);
                    }
                }
            }
            else if (!isAddMode)
            {
                //if (this.Config.useRectangularMarquee)
                //{
                //    return;
                //}
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
            if (IsAllowedToSow())
            {
                foreach (var tile in targetedTiles)
                {
                    Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
                    e.SpriteBatch.Draw(crosshairTexture, screenPos, null, new Color(196, 241, 190), 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
                }
            }
        }


        private HashSet<Vector2> GetEdgesRectangularMarquee(Vector2 startTile, Vector2 lastTile)
        {
            float minX = Math.Min(startTile.X, lastTile.X);
            float maxX = Math.Max(startTile.X, lastTile.X);
            float minY = Math.Min(startTile.Y, lastTile.Y);
            float maxY = Math.Max(startTile.Y, lastTile.Y);

            HashSet<Vector2> edges = new HashSet<Vector2>();

            // Add top edge coordinates
            for (float x = minX; x <= maxX; x++)
                edges.Add(new Vector2(x, minY));

            // Add bottom edge coordinates
            for (float x = minX; x <= maxX; x++)
                edges.Add(new Vector2(x, maxY));

            // Add left edge coordinates
            for (float y = minY; y <= maxY; y++)
                edges.Add(new Vector2(minX, y));

            // Add right edge coordinates
            for (float y = minY; y <= maxY; y++)
                edges.Add(new Vector2(maxX, y));

            return edges;
        }


        private void RenderRectangularMarquee(RenderedWorldEventArgs e)
        {
            if (IsAllowedToSow())
            {
                if (startTile is not null && lastTile is not null)
                {
                    HashSet<Vector2> edges = GetEdgesRectangularMarquee(startTile.Value, lastTile.Value);
                    foreach (var tile in edges)
                    {
                        Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
                        e.SpriteBatch.Draw(marqueeTexture, screenPos, null, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
                    }
                }
            }
        }


        private bool IsAllowedToSow()
        {
            return Game1.currentLocation is Farm || Game1.currentLocation.Name == "Greenhouse" || Game1.currentLocation.Name == "IslandWest" || this.Config.allowSowingOutsideFarm;
        }


        private void PlaceToTile(Item selectedItem, Vector2 tile, bool isFertilizer)
        {
            var location = Game1.currentLocation;
            // Ensure the location is one of the desired ones
            if (IsAllowedToSow())
            {
                // Attempt to get the HoeDirt object at the specified tile
                if (location.terrainFeatures.TryGetValue(tile, out var terrainFeature) && terrainFeature is HoeDirt hoeDirt)
                {
                    if (isFertilizer)
                    {
                        if (this.Config.preventFertilizerOnEmptySoil && IsUnplantedTile(tile))
                        {
                            return;
                        }
                    }

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
                //else
                //{
                //    Monitor.Log($"No HoeDirt found at tile {tile} in {location.Name}.", LogLevel.Warn);
                //}
            }
            //else
            //{
            //    Monitor.Log("Player is not in the Farm, Greenhouse, or Ginger Island.", LogLevel.Warn);
            //}
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
                if (!this.Config.alwaysSowOnAnyTilledSoil)
                {
                    return;
                }
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
                if (this.Config.alwaysSowOnAnyTilledSoil && IsTilledTile(currtile) && IsUnplantedTile(currtile) && IsHoldingSeeds())
                {
                    PlaceToTile(selectedItem, currtile, false);
                }
                if (this.Config.alwaysSowOnAnyTilledSoil && IsTilledTile(currtile) && IsUnfertilziedTile(currtile) && IsHoldingFertilizer())
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