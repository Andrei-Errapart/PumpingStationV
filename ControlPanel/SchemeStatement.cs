using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ControlPanel
{
    public class SchemeStatement
    {
        public enum TYPE
        {
            SHOW,
            HIDE,
            BLINK,
            DISPLAY,
        };

        public readonly ControlPanelViewModel Context;
        public readonly TYPE Type;
        public readonly SchemeLayer Layer;
        public readonly MuteButton MuteButton;
        public SchemeExpression ConditionOrExpression;

        public SchemeStatement(
            ControlPanelViewModel Context,
            TYPE Type,
            string LayerName)
        {
            this.Context = Context;
            this.Type = Type;
            SchemeLayer layer = null;
            MuteButton mutebutton = null;
            Context.Layers.TryGetValue(LayerName, out layer);
            this.Layer = layer;
            if (layer == null)
            {
                Context.LogLine("Scheme program: layer '" + LayerName + "' not found, ignoring statement.");
            }
            else
            {
                if (this.Type == TYPE.BLINK)
                {
                    // alarm detected!
                    var bounds = layer.Layer.Bounds;
                    // New button.
                    var size = 48.0;
                    mutebutton = new MuteButton();
                    mutebutton.Opacity = 0.7;
                    mutebutton.SetValue(System.Windows.Controls.Canvas.LeftProperty, bounds.Right + size/4);
                    mutebutton.SetValue(System.Windows.Controls.Canvas.TopProperty, bounds.Top - size/2);
                    mutebutton.Width = size;
                    mutebutton.Height = size;
                    Context.MainCanvas.Children.Add(mutebutton);
                    mutebutton.SetAlarmState(false);
                }
            }
            this.MuteButton = mutebutton;
        }

        /// <summary>
        /// Execute this statement. It is that simple.
        /// </summary>
        public void Execute()
        {
            _SourceSignals.Clear();
            int value = ConditionOrExpression == null ? 1 : ConditionOrExpression.Evaluate(_SourceSignals, true);
            if (Layer != null)
            {
                switch (Type)
                {
                    case TYPE.SHOW:
                        Layer.IsVisible = value != 0;
                        break;
                    case TYPE.HIDE:
                        Layer.IsVisible = value == 0;
                        break;
                    case TYPE.BLINK:
                        Layer.IsBlinking = value != 0;
                        if (MuteButton != null)
                        {
                            MuteButton.SetAlarmState(Layer.IsBlinking);
                        }
                        // Blinking objects must be invisible when not blinking....
                        if (Layer.IsBlinking)
                        {
                            // We shall mark the source signals.
                            foreach (var ss in _SourceSignals)
                            {
                                ss.DisplayIsAlarm = true;
                            }
                            _PrevSourceSignals.AddRange(_SourceSignals);
                        }
                        else
                        {
                            Layer.IsVisible = false;
                            // Previous source signals need unmarking.
                            foreach (var ss in _PrevSourceSignals)
                            {
                                ss.DisplayIsAlarm = false;
                            }
                            _PrevSourceSignals.Clear();
                        }
                        break;
                    case TYPE.DISPLAY:
                        // TODO: implement this feature.
                        break;
                }
            }
        }

        List<IOSignal> _SourceSignals = new List<IOSignal>();
        List<IOSignal> _PrevSourceSignals = new List<IOSignal>();
    }
}
