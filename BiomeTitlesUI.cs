using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using System.Linq;
using System.Collections.Generic;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ID;

namespace BTitles
{
    public class BiomeTitlesUI : UIState
    {
        public GeneralConfig Config { get; private set; }

        private Point _dragStartMousePosition;
        private Vector2 _dragStartCustomPosition;

        // Data storage
        internal Dictionary<string, BiomeEntry> BiomeDictionary;
        internal List<Func<string, dynamic>> DynamicBiomeProviders;
        internal List<Func<Player, string>> DynamicBiomeCheckFunctions;
        internal List<Func<Player, string>> MiniBiomeCheckFunctions;
        internal List<Func<Player, string>> BiomeCheckFunctions;

        // Widgets
        private BiomeTitle _biomeTitle;
        private BiomeTitle _biomeSubTitle;
        
        // Default widgets
        private BiomeTitle _biomeTitleDefault;
        private BiomeTitle _biomeSubTitleDefault;

        // Animation stuff
        private Action<float, AnimationConfig, BiomeTitle, BiomeTitle> _animateFunc;
        private AnimationConfig _animationConfig = new AnimationConfig();
        
        // UI State
        private string _currentBiome = "";
        private bool _currentBiomeDynamic = false;
        private BiomeEntry _currentBiomeEntry = null;
        private bool _isDragging;
        private float _displayTimer;
        private float _biomeCheckTimer = float.PositiveInfinity;

        // Debug data
        private double _debugLastUpdateDuration;
        private double _debugLastBiomeCheckDuration;

        private Vector2 _debugStringPos;

        public BiomeTitlesUI()
        {
            Config = ModContent.GetInstance<GeneralConfig>();
            Config.OnPropertyChanged += ConfigPropertyChanged;
            
            switch (Config.TitleAnimation)
            {
                case TitleAnimationType.None:
                    _animateFunc = TitleAnimations.AnimateNone;
                    break;
                case TitleAnimationType.ShowFade:
                    _animateFunc = TitleAnimations.AnimateShowFade;
                    break;
                case TitleAnimationType.ShowFadeSwipe:
                    _animateFunc = TitleAnimations.AnimateShowFadeSwipe;
                    break;
            }
            _animationConfig.Duration = Config.VisibilityDuration;
            _animationConfig.InTime = Config.TransitionInDuration;
            _animationConfig.OutTime = Config.TransitionOutDuration;

            _biomeTitleDefault = new BasicBiomeTitle
            {
                Background = new SegmentedHorizontalPanel
                {
                    Texture = ModContent.Request<Texture2D>("BTitles/Resources/Textures/TitleBackgroundDefault", AssetRequestMode.ImmediateLoad).Value,
                    LeftSegmentSize = 44 / 514f,
                    RightSegmentSize = 44 / 514f,
                    MiddleSegmentSize = 46 / 514f,
                    Width = { Percent = 1 },
                    Height = { Percent = 1 },
                    LeftContentPadding = 44,
                    RightContentPadding = 44
                },
                
                CustomReset = self =>
                {
                    self.ContentOffset = new Vector2(0, 1);
                }
            };
            ((SegmentedHorizontalPanel)((BasicBiomeTitle)_biomeTitleDefault).Background).RecalculateSourceRects();
            
            _biomeSubTitleDefault = new BasicBiomeTitle
            {
                Background = new SegmentedHorizontalPanel
                {
                    Texture = ModContent.Request<Texture2D>("BTitles/Resources/Textures/SubTitleBackgroundDefault", AssetRequestMode.ImmediateLoad).Value,
                    LeftSegmentSize = 44 / 514f,
                    RightSegmentSize = 44 / 514f,
                    MiddleSegmentSize = 46 / 514f,
                    Width = { Percent = 1 },
                    Height = { Percent = 1 },
                    LeftContentPadding = 44,
                    RightContentPadding = 44
                }
            };
            ((SegmentedHorizontalPanel)((BasicBiomeTitle)_biomeSubTitleDefault).Background).RecalculateSourceRects();
        }

        private void ConfigPropertyChanged(string name)
        {
            switch (name)
            {
                case nameof(GeneralConfig.Position):
                    ApplyPositionFromConfig();
                    Recalculate();
                    break;
                case nameof(GeneralConfig.CustomPositionX):
                    if (Config.Position == PositionOption.Custom)
                    {
                        ApplyPositionFromConfig();
                        Recalculate();
                    }
                    break;
                case nameof(GeneralConfig.CustomPositionY):
                    if (Config.Position == PositionOption.Custom)
                    {
                        ApplyPositionFromConfig();
                        Recalculate();
                    }
                    break;
                case nameof(GeneralConfig.Scale):
                    SetupTitleVisuals();
                    Recalculate();
                    break;
                case nameof(GeneralConfig.UseCustomTextColors):
                    SetupTitleVisuals();
                    break;
                case nameof(GeneralConfig.ShowIcons):
                    SetupTitleVisuals();
                    Recalculate();
                    break;
                case nameof(GeneralConfig.DisplaySubtitle):
                    RespawnSubTitle();
                    SetupTitleVisuals();
                    Recalculate();
                    break;
                case nameof(GeneralConfig.EnableBackgrounds):
                    SetupTitleVisuals();
                    Recalculate();
                    break;
                case nameof(GeneralConfig.CustomBiomeNames):
                    RespawnTitle();
                    SetupTitleVisuals();
                    Recalculate();
                    break;
                case nameof(GeneralConfig.TitleAnimation):
                    switch (Config.TitleAnimation)
                    {
                        case TitleAnimationType.ShowFade:
                            _animateFunc = TitleAnimations.AnimateShowFade;
                            break;
                        case TitleAnimationType.ShowFadeSwipe:
                            _animateFunc = TitleAnimations.AnimateShowFadeSwipe;
                            break;
                    }

                    _biomeTitle?.Reset();
                    _biomeSubTitle?.Reset();
                    
                    SetupTitleVisuals();
                    break;
                case nameof(GeneralConfig.VisibilityDuration):
                    _animationConfig.Duration = Config.VisibilityDuration;
                    
                    RespawnTitle();
                    RespawnSubTitle();
                    SetupTitleVisuals();
                    Recalculate();
                    break;
                case nameof(GeneralConfig.TransitionInDuration):
                    _animationConfig.InTime = Config.TransitionInDuration;
                    break;
                case nameof(GeneralConfig.TransitionOutDuration):
                    _animationConfig.OutTime = Config.TransitionOutDuration;
                    break;
            }
        }

        public void ResetBiome()
        {
            _currentBiome = "";
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            // var dimensions = GetDimensions();
            // spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)dimensions.X, (int)dimensions.Y, (int)dimensions.Width, (int)dimensions.Height), Color.Blue * 0.5f);

            if (false)
            {
                _debugStringPos = new Vector2(560, 200);
                DrawDebugString(spriteBatch, "Biome check timer: " + _biomeCheckTimer + " s");
                DrawDebugString(spriteBatch, "Display timer: " + _displayTimer + " s");
                DrawDebugString(spriteBatch, "Last biome check duration: " + _debugLastBiomeCheckDuration + " ms");
                DrawDebugString(spriteBatch, "Last update duration: " + _debugLastUpdateDuration + " ms");
                DrawDebugString(spriteBatch, "Current biome: " + _currentBiome);
                DrawDebugString(spriteBatch, "Is dynamic biome: " + _currentBiomeDynamic);
            }
        }

        protected void DrawDebugString(SpriteBatch spriteBatch, string text)
        {
            spriteBatch.DrawString(FontAssets.MouseText.Value, text, _debugStringPos, Color.White);
            _debugStringPos.Y += 30;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            DateTime updateStart = DateTime.Now;

            UpdateDragging();

            float timeDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Update timers
            _biomeCheckTimer += timeDelta;
            _displayTimer += timeDelta;

            if (_biomeCheckTimer >= Config.BiomeCheckDelay)
            {
                CheckNewBiome();
            }

            // Check if a boss is alive and the configuration to hide titles while a boss is alive is enabled
            bool bossIsAlive = Main.npc.Any(npc => npc.active && npc.boss);
            if (Config.HideWhileBossIsAlive && bossIsAlive)
            {
                // Hide titles if a boss is alive
                if (_biomeTitle != null)
                {
                    _biomeTitle.Opacity = 0;
                }

                if (_biomeSubTitle != null)
                {
                    _biomeSubTitle.Opacity = 0;
                }
            }
            else if (Config.HideWhileInventoryOpen && Main.playerInventory)
            {
                // Hide titles if the inventory is open
                if (_biomeTitle != null)
                {
                    _biomeTitle.Opacity = 0;
                }

                if (_biomeSubTitle != null)
                {
                    _biomeSubTitle.Opacity = 0;
                }
            }
            else
            {
                // Show titles when neither condition is met
                _animateFunc(_displayTimer, _animationConfig, _biomeTitle, _biomeSubTitle);
            }

            _debugLastUpdateDuration = (DateTime.Now - updateStart).TotalMilliseconds;
        }

        public override void LeftMouseDown(UIMouseEvent evt)
        {
            if (Config.Position != PositionOption.Custom || !Config.EnableDraggableTitle) return;
            
            if (_biomeTitle?.GetDimensions().ToRectangle().Contains(Main.mouseX, Main.mouseY) ?? 
                _biomeSubTitle?.GetDimensions().ToRectangle().Contains(Main.mouseX, Main.mouseY) ??
                false)
            {
                _isDragging = true;
                Main.isMouseLeftConsumedByUI = true;
                _dragStartMousePosition = new Point(Main.mouseX, Main.mouseY);
                _dragStartCustomPosition = new Vector2(Config.CustomPositionX, Config.CustomPositionY);
            }
            
            base.LeftMouseDown(evt);
        }

        private void UpdateDragging()
        {
            if (!_isDragging) return;
            
            if (Main.mouseLeftRelease || Config.Position != PositionOption.Custom || !Config.EnableDraggableTitle)
            {
                _isDragging = false;
                return;
            }

            Config.CustomPositionX = _dragStartCustomPosition.X + (Main.mouseX - _dragStartMousePosition.X) / (float)Main.screenWidth;
            Config.CustomPositionY = _dragStartCustomPosition.Y + (Main.mouseY - _dragStartMousePosition.Y) / (float)Main.screenHeight;
        }

        private void CheckNewBiome()
        {
            _biomeCheckTimer = 0;
            _currentBiomeDynamic = false;
            
            Player player = Main.LocalPlayer;
            string newBiome = "";

            DateTime biomeCheckStart = DateTime.Now;

            for (int i = 0; i < DynamicBiomeCheckFunctions.Count && newBiome == ""; i++)
            {
                newBiome = DynamicBiomeCheckFunctions[i](player);
            }
            
            if (newBiome != "")
            {
                _currentBiomeDynamic = true;
            }
            
            for (int i = 0; i < MiniBiomeCheckFunctions.Count && newBiome == ""; i++)
            {
                newBiome = MiniBiomeCheckFunctions[i](player);
            }
            
            for (int i = 0; i < BiomeCheckFunctions.Count && newBiome == ""; i++)
            {
                newBiome = BiomeCheckFunctions[i](player);
            }
            
            _debugLastBiomeCheckDuration = (DateTime.Now - biomeCheckStart).TotalMilliseconds;

            if (newBiome != _currentBiome)
            {
                _currentBiome = newBiome;
                
                if (_currentBiomeDynamic)
                {
                    dynamic biome = null;
                    for (int i = 0; i < DynamicBiomeProviders.Count && biome == null; i++)
                    {
                        biome = DynamicBiomeProviders[i](_currentBiome);
                    }

                    if (Integration.IntegrateBiome(biome, ignoreKey: true)?.Value is BiomeEntry validBiomeEntry)
                    {
                        _currentBiomeEntry = validBiomeEntry;
                        _currentBiomeEntry.Key = _currentBiome;
                    }
                    else
                    {
                        _currentBiomeEntry = null;
                    }
                }
                else
                {
                    BiomeDictionary.TryGetValue(_currentBiome, out _currentBiomeEntry);
                }
                
                _displayTimer = 0;

                RespawnTitle();
                RespawnSubTitle();
                
                ApplyPositionFromConfig();
                SetupTitleVisuals();
                Recalculate();
            }
        }
        
        private void RespawnTitle()
        {
            if (_biomeTitle != null)
            {
                _biomeTitle.Remove();
                _biomeTitle = null;
            }

            if (_currentBiomeEntry != null && (_displayTimer < Config.VisibilityDuration || Config.VisibilityDuration == 0))
            {
                _biomeTitle = _currentBiomeEntry.TitleWidget ?? _biomeTitleDefault;
                _biomeTitle.Reset();
                Append(_biomeTitle);
            }
        }

        private void RespawnSubTitle()
        {
            if (_biomeSubTitle != null)
            {
                _biomeSubTitle.Remove();
                _biomeSubTitle = null;
            }
                
            if (_currentBiomeEntry != null && Config.DisplaySubtitle && (_displayTimer < Config.VisibilityDuration || Config.VisibilityDuration == 0) && !string.IsNullOrWhiteSpace(_currentBiomeEntry.SubTitle))
            {
                _biomeSubTitle = _currentBiomeEntry.SubTitleWidget ?? _biomeSubTitleDefault;
                _biomeSubTitle.Reset();
                Append(_biomeSubTitle);
            }
        }
        
        private void ApplyPositionFromConfig()
        {
            switch (Config.Position)
            {
                case PositionOption.Top:
                    Left.Set(0, 0);
                    Top.Set(80, 0);
                    HAlign = 0.5f;
                    VAlign = 0;
                    Recalculate();
                    
                    return;
                case PositionOption.Bottom:
                    Left.Set(0, 0);
                    Top.Set(-80, 0);
                    HAlign = 0.5f;
                    VAlign = 1;
                    Recalculate();
                    
                    return;
                case PositionOption.RPGStyle:
                    Left.Set(120, 0);
                    Top.Set(-80, 0);
                    HAlign = 0;
                    VAlign = 1;
                    Recalculate();
                    
                    return;
                case PositionOption.Custom:
                    Left.Set(0, 0);
                    Top.Set(0, 0);
                    HAlign = Config.CustomPositionX;
                    VAlign = Config.CustomPositionY;
                    Recalculate();
                    
                    return;
            }
        }

        void SetupTitleVisuals()
        {
            float exactScale = Config.Scale.ToFloat();
            
            float offset = 10 * exactScale;

            if (_currentBiomeEntry == null) return;

            if (_biomeTitle != null)
            {
                _biomeTitle.HAlign = 0.5f;
                _biomeTitle.Text = GetActualTitleName(_currentBiomeEntry);
                _biomeTitle.TextColor = Config.UseCustomTextColors ? _currentBiomeEntry.TitleColor : Color.White;
                _biomeTitle.TextStrokeColor = Config.UseCustomTextColors ? _currentBiomeEntry.StrokeColor : Color.Black;
                _biomeTitle.Icon = Config.ShowIcons ? _currentBiomeEntry.Icon : null;
                _biomeTitle.FontScale = 1.4f;
                _biomeTitle.Scale = exactScale;
                _biomeTitle.SetBackgroundEnabled(Config.EnableBackgrounds);
                _biomeTitle.SetWidthHeightAuto();
                _biomeTitle.Recalculate();
            }

            if (_biomeSubTitle != null)
            {
                _biomeSubTitle.Top.Set(_biomeTitle != null ? _biomeTitle.GetDimensions().Height + offset : 0, 0);
                _biomeSubTitle.HAlign = 0.5f;
                _biomeSubTitle.Text = _currentBiomeEntry.SubTitle;
                _biomeSubTitle.TextColor = Color.White;
                _biomeSubTitle.TextStrokeColor = Color.Black;
                _biomeSubTitle.Scale = exactScale;
                _biomeSubTitle.SetBackgroundEnabled(Config.EnableBackgrounds);
                _biomeSubTitle.SetWidthHeightAuto();
                _biomeSubTitle.Recalculate();
            }
            
            Width.Set(Math.Max(_biomeTitle?.GetDimensions().Width ?? 0, _biomeSubTitle?.GetDimensions().Width ?? 0), 0);
            Height.Set((_biomeTitle?.GetDimensions().Height ?? 0) + (_biomeSubTitle?.GetDimensions().Height ?? 0) + (_biomeTitle != null && _biomeSubTitle != null ? offset : 0), 0);
        }

        private string GetActualTitleName(BiomeEntry biomeEntry)
        {
            string customName = Config.CustomBiomeNames.FirstOrDefault(name => name.CurrentName == biomeEntry.Title)?.NewName;

            if (customName != null) return customName;

            if (Language.ActiveCulture.Name != "en-US")
            {
                string translationKey = $"Mods.BiomeTitles.Title.{biomeEntry.LocalizationScope}.{biomeEntry.Key.Replace(" ", "_")}";

                if (Language.Exists(translationKey))
                {
                    string translatedName = Language.GetTextValue(translationKey);
            
                    customName = Config.CustomBiomeNames.FirstOrDefault(name => name.CurrentName == translatedName)?.NewName;

                    return customName ?? translatedName;
                }
            }

            return biomeEntry.Title;
        }
    }
}
