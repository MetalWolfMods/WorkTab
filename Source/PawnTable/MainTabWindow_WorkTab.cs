﻿// Karel Kroeze
// PawnTable_WorkTab.cs
// 2017-05-22

using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using static WorkTab.Constants;
using static WorkTab.InteractionUtilities;
using static WorkTab.Resources;

namespace WorkTab
{
    public class MainTabWindow_WorkTab: MainTabWindow_PawnTable
    {
        protected override PawnTableDef PawnTableDef => PawnTableDefOf.Work;
        private static FieldInfo _tableFieldInfo;
        private static List<int> _selectedHours = TimeUtilities.WholeDay;
        private static int _visibleHour = -1;

        public static List<int> SelectedHours => PriorityManager.Get.ShowScheduler
            ? _selectedHours
            : TimeUtilities.WholeDay;

        public static int VisibleHour => PriorityManager.Get.ShowScheduler
            ? _visibleHour
            : -1;

        public static void AddSelectedHour( int hour, bool replace )
        {
            if (replace)
                _selectedHours.Clear();
            if (replace || !_selectedHours.Contains( hour ))
                _selectedHours.Add( hour );
            _visibleHour = hour;
        }

        public static void RemoveSelectedHour( int hour )
        {
            if ( _selectedHours.Contains( hour ) )
                _selectedHours.Remove( hour );

            if ( _visibleHour == hour )
                _visibleHour = hour;
        }
        
        public static void SelectWholeDay()
        {
            _selectedHours = TimeUtilities.WholeDay;
            _visibleHour = -1;
        }

        static MainTabWindow_WorkTab()
        {
            _tableFieldInfo = typeof(MainTabWindow_PawnTable).GetField("table",
                                                                          BindingFlags.Instance | BindingFlags.NonPublic);
            if (_tableFieldInfo == null)
                throw new NullReferenceException("table field not found!");
        }
        public MainTabWindow_WorkTab() { _instance = this; }

        private static MainTabWindow_WorkTab _instance;
        public static MainTabWindow_WorkTab Instance => _instance;

        public override void PostOpen()
        {
            base.PostOpen();
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            RebuildTable();
        }
        
        public PawnTable Table
        {
            get { return _tableFieldInfo.GetValue(this) as PawnTable; }
            private set { _tableFieldInfo.SetValue(this, value); }
        }

        public static void RebuildTable()
        {
            if ( _instance == null )
            {
                Logger.Debug( "calling RebuildTable on a null instance" );
                return;
            }

            // get columns
            var columns = Columns; 
            
            // update alternating vertical positions
            bool moveNextDown = false;
            foreach ( PawnColumnDef columnDef in columns )
            {
                var worker = columnDef.Worker as IAlternatingColumn;
                if ( worker != null )
                {
                    worker.MoveDown = moveNextDown;
                    moveNextDown = !moveNextDown;
                }
                else
                {
                    moveNextDown = false;
                }
            }

            // rebuild table
            Instance.Table = new PawnTable( columns, () => Instance.Pawns, 998, UI.screenWidth - (int)(Instance.Margin * 2f), 0,
                                   (int)(UI.screenHeight - 35 - Instance.ExtraBottomSpace - Instance.ExtraTopSpace - Instance.Margin * 2f));

            // force recache of table sizes: set the table to be dirty, then get the size - which calls the private recache routine.
            Instance.Table.SetDirty();
            var tmp = Instance.Table.Size;

            // force recache of timeBarRect
            Instance._timeBarRect = default( Rect );
        }

        private static List<PawnColumnDef> Columns
        {
            get
            {
                // get clean copy of base columns
                List<PawnColumnDef> columns = new List<PawnColumnDef>(_instance.PawnTableDef.columns);

                // add workgiver columns for expanded worktypes
                var templist = new List<PawnColumnDef>(columns);
                foreach (PawnColumnDef column in templist)
                {
                    var expandable = column.Worker as IExpandableColumn;
                    if (expandable != null && expandable.Expanded)
                    {
                        var index = columns.IndexOf(column);
                        columns.InsertRange(index + 1, expandable.ChildColumns);
                    }
                }

                return columns;
            }
        }

        public override void DoWindowContents( Rect rect )
        {
            base.DoWindowContents( rect );
            if (Event.current.type == EventType.Layout)
                return;

            DoToggleButtons( rect );
            DoPriorityLabels( rect );
            if (PriorityManager.Get.ShowScheduler)
                DoTimeBar( rect );
        }

        private void DoTimeBar( Rect rect )
        {
            // set up rects
            Rect bar = TimeBarRect;
            Rect buttons = new Rect(rect.xMin, bar.yMin + bar.height / 3f, bar.xMin - rect.xMin, bar.height * 2/3f);
            Rect button = new Rect(buttons.xMax - buttons.height, buttons.yMin, buttons.height, buttons.height);

            // split the available area into rects. bottom 2/3's are used for 'buttons', with text for times.
            float hourWidth = bar.width / GenDate.HoursPerDay;
            float barheight = bar.height * 2 / 3f;
            float timeIndicatorSize = bar.height * 2 / 3f;
            float lastLabelPosition = 0f;
            Rect hourRect = new Rect( bar.xMin, bar.yMax - barheight, hourWidth, barheight );

            // draw buttons
            TooltipHandler.TipRegion(button, "WorkTab.SelectWholeDayTip".Translate());
            if (Widgets.ButtonImage( button, PrioritiesWholeDay, Color.white, GenUI.MouseoverColor ))
                SelectWholeDay();
            button.x -= button.height + Constants.Margin;
            TooltipHandler.TipRegion(button, "WorkTab.SelectCurrentHourTip".Translate());
            if (Widgets.ButtonImage(button, Now, Color.white, GenUI.MouseoverColor))
                AddSelectedHour(GenLocalDate.HourOfDay(Find.VisibleMap), true);

            // draw first tick
            GUI.color = Color.grey;
            Widgets.DrawLineVertical(hourRect.xMin, hourRect.yMin + hourRect.height * 1 / 2f, hourRect.height * 1 / 2f);

            // draw horizontal line ( y - 1 because canvas gets clipped on bottom )
            Widgets.DrawLineHorizontal(bar.xMin, bar.yMax - 1, bar.width);
            GUI.color = Color.white;

            // label and rect
            string label;
            Rect labelRect;

            for (int hour = 0; hour < GenDate.HoursPerDay; hour++)
            {
                bool selected = SelectedHours.Contains(hour);
                bool focused = hour == VisibleHour;

                // print major tick
                GUI.color = Color.grey;
                Widgets.DrawLineVertical(hourRect.xMax, hourRect.yMin + hourRect.height * 1 / 2f, hourRect.height * 1 / 2f);

                // print minor ticks
                Widgets.DrawLineVertical(hourRect.xMin + hourRect.width * 1 / 4f, hourRect.yMin + hourRect.height * 3 / 4f, hourRect.height * 1 / 4f);
                Widgets.DrawLineVertical(hourRect.xMin + hourRect.width * 2 / 4f, hourRect.yMin + hourRect.height * 3 / 4f, hourRect.height * 1 / 4f);
                Widgets.DrawLineVertical(hourRect.xMin + hourRect.width * 3 / 4f, hourRect.yMin + hourRect.height * 3 / 4f, hourRect.height * 1 / 4f);
                GUI.color = Color.white;

                // create and draw labelrect - but only if the last label isn't too close
                if ( hourRect.xMin - lastLabelPosition > MinTimeBarLabelSpacing )
                {
                    label = hour.FormatHour();
                    labelRect = new Rect(0f, bar.yMin + bar.height * 1 / 3f, label.NoWrapWidth(), bar.height * 2 / 3f);
                    labelRect.x = hourRect.xMin - labelRect.width / 2f;
                    UIUtilities.Label(labelRect, label, Color.grey, GameFont.Tiny, TextAnchor.UpperCenter);

                    lastLabelPosition = labelRect.xMax;
                }

                // draw hour rect with mouseover + interactions
                Widgets.DrawHighlightIfMouseover(hourRect);

                // set/remove focus (LMB and any other MB respectively)
                if (Mouse.IsOver(hourRect))
                {
                    if (Input.GetMouseButton(0))
                        AddSelectedHour( hour, Event.current.shift );

                    if ( Input.GetMouseButton( 1 ) )
                        RemoveSelectedHour( hour );

                    // handle tooltip
                    var selectedString = selected
                                                      ? "WorkTab.Selected".Translate()
                                                      : "WorkTab.NotSelected".Translate();
                    var interactionString = "";
                    if ( selected )
                    {
                        interactionString += "WorkTab.RightClickToDeselect".Translate();
                        if ( focused )
                            interactionString += "\n" + "WorkTab.ClickToFocus".Translate();
                    }
                    else
                        interactionString += "WorkTab.ClickToSelect".Translate();

                    TooltipHandler.TipRegion( hourRect,
                                              "WorkTab.SchedulerHourTip".Translate( hour.FormatHour(),
                                                                                    ( hour + 1 % GenDate.HoursPerDay ).FormatHour(),
                                                                                    selectedString,
                                                                                    interactionString ) );

                }

                // if this is currently the 'main' timeslot, and not the actual time, draw an eye
                if ( focused && hour != GenLocalDate.HourOfDay(Find.VisibleMap) )
                {
                    Rect eyeRect = new Rect(hourRect.center.x - timeIndicatorSize * 1 / 2f, hourRect.yMax - timeIndicatorSize - hourRect.height * 1 / 6f, timeIndicatorSize, timeIndicatorSize);
                    GUI.DrawTexture(eyeRect, PinEye);
                }

                // also highlight all selected timeslots
                if ( selected )
                    Widgets.DrawHighlightSelected( hourRect );

                // advance rect
                hourRect.x += hourRect.width;
            }

            // draw final label
            label = 0.FormatHour();
            labelRect = new Rect(0f, bar.yMin + bar.height * 1 / 3f, label.NoWrapWidth(), bar.height * 2 / 3f);
            labelRect.x = hourRect.xMin - labelRect.width / 2f;
            UIUtilities.Label(labelRect, label, Color.grey, GameFont.Tiny, TextAnchor.UpperCenter);

            // draw current time indicator
            float curTimeX = GenLocalDate.DayPercent(Find.VisibleMap) * bar.width;
            Rect curTimeRect = new Rect(bar.xMin + curTimeX - timeIndicatorSize * 1 / 2f, hourRect.yMax - timeIndicatorSize - hourRect.height * 1 / 6f, timeIndicatorSize, timeIndicatorSize);
            GUI.DrawTexture(curTimeRect, PinClock);
        }


        private Rect _timeBarRect = default( Rect );
        public Rect TimeBarRect
        {
            get
            {
                if ( _timeBarRect == default( Rect ) )
                    RecacheTimeBarRect();

                return _timeBarRect;
            }
        }

        public void RecacheTimeBarRect()
        {
            var widths = Traverse.Create(Table).Field("cachedColumnWidths").GetValue<List<float>>();
            var columns = Table.ColumnsListForReading;
            float start = 0;
            float width = 0;

            // loop over columns, initially add any column that is not a workbox to the start, but not after we've seen a workbox.
            // Add widths for workboxes to width. 
            // NOTE: This assumes a single contiguous block of workboxes!
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i].Worker;
                if (column is PawnColumnWorker_WorkType || column is PawnColumnWorker_WorkGiver)
                    width += widths[i];
                else if (width < 1)
                    start += widths[i];
            }

            // build the rect
            _timeBarRect = new Rect(start,
                windowRect.height - base.ExtraBottomSpace, // note that we're not subtracting the time bar height itself, as at this point the window's height has not yet been updated to include it.
                width,
                TimeBarHeight);

            Logger.Debug("created time bar rect: " + _timeBarRect);
        }

        private void DoPriorityLabels( Rect canvas )
        {
            Rect priorityLabelRect = new Rect( canvas.width / 3f - PriorityLabelSize.x / 2f,
                                               canvas.yMin,
                                               PriorityLabelSize.x,
                                               PriorityLabelSize.y );

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;

            Widgets.Label( priorityLabelRect, "<= " + "HigherPriority".Translate());

            priorityLabelRect.x += canvas.width / 3f;
            Widgets.Label( priorityLabelRect, "LowerPriority".Translate() + " =>");

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DoToggleButtons( Rect canvas )
        {
            Rect rect = new Rect( canvas.xMax - 30f, canvas.yMin, 30f, 30f );

            ButtonImageToggle(() => PriorityManager.Get.ShowPriorities, val => PriorityManager.Set.ShowPriorities = val, rect,
                               "WorkTab.PrioritiesDetailed".Translate(), PrioritiesDetailed,
                               "WorkTab.PrioritiesSimple".Translate(), PrioritiesSimple );
            rect.x -= 30f + Margin;

            ButtonImageToggle( () => PriorityManager.Get.ShowScheduler, val => PriorityManager.Set.ShowScheduler = val, rect,
                               "WorkTab.PrioritiesTimed".Translate(), PrioritiesTimed,
                               "WorkTab.PrioritiesWholeDay".Translate(), PrioritiesWholeDay);
            rect.x -= 30f + Margin;
        }

        protected override float ExtraBottomSpace => PriorityManager.Get.ShowScheduler
            ? base.ExtraBottomSpace / 2f + TimeBarHeight // slightly less margin if we're showing the scheduler, as it already takes quite a sizeable chunk of screen space
            : base.ExtraBottomSpace;
        protected override float ExtraTopSpace => base.ExtraTopSpace + Constants.ExtraTopSpace;
    }
}