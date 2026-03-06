using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HexcellsQoL;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private static Plugin instance;
    private Harmony harmony;

    Texture2D hintTexture;
    Texture2D hypotheticalBlueTexture;
    Texture2D hypotheticalBlueHintTexture;
    Texture2D hypotheticalBlackTexture;
    Texture2D hypotheticalBlackHintTexture;

    List<UndoState> undo = new List<UndoState>();
    List<RedoState> redo = new List<RedoState>();

    private interface UndoState
    {
        public RedoState Undo();
    }

    private interface RedoState
    {
        public UndoState Redo();
    }

    private class DestroyUndoState : UndoState
    {
        public Vector3 position;
        public bool containsShapeBlock;
        public HexState hexState;

        public RedoState Undo()
        {
            // Look up game state.
            GameObject hexGridOverlay = GameObject.Find("Hex Grid Overlay");
            GameObject editorFunctions = GameObject.Find("Editor Functions");
            GameObject hexScoring = GameObject.Find("Score Text");
            GameObject musicDirector = GameObject.Find("Music Director(Clone)");
            if (hexGridOverlay == null || editorFunctions == null || hexScoring == null || musicDirector == null)
            {
                return null;
            }

            // Recreate orange hex.
            GameObject orangeHexTemplate = editorFunctions.GetComponent<EditorFunctions>().orangeHex;
            GameObject orangeHex = Instantiate(orangeHexTemplate, position, orangeHexTemplate.transform.rotation, hexGridOverlay.transform);
            HexBehaviour hexBehaviourComponent = orangeHex.GetComponent<HexBehaviour>();
            hexBehaviourComponent.containsShapeBlock = containsShapeBlock;
            hexState.SaveTo(hexBehaviourComponent);

            // Update score text.
            HexScoring hexScoringComponent = hexScoring.GetComponent<HexScoring>();
            hexScoringComponent.tilesRemoved--;
            if (containsShapeBlock)
            {
                hexScoringComponent.numberOfCorrectTilesFound -= 2;
                hexScoringComponent.CorrectTileFound();
            }

            // Create redo state.
            DestroyRedoState redoState = new DestroyRedoState();
            redoState.hexBehaviourComponent = hexBehaviourComponent;

            // Signal success.
            musicDirector.GetComponent<MusicDirector>().PlayMouseOverSound();

            return redoState;
        }
    }

    private class DestroyRedoState : RedoState
    {
        public HexBehaviour hexBehaviourComponent;

        public UndoState Redo()
        {
            // Look up game state.
            GameObject hexScoring = GameObject.Find("Score Text");
            GameObject musicDirector = GameObject.Find("Music Director(Clone)");
            if (hexScoring == null || musicDirector == null)
            {
                return null;
            }

            // Update score text.
            HexScoring hexScoringComponent = hexScoring.GetComponent<HexScoring>();
            hexScoringComponent.tilesRemoved++;
            if (hexBehaviourComponent.containsShapeBlock)
            {
                hexScoringComponent.CorrectTileFound();
            }

            // Create undo state.
            DestroyUndoState undoState = new DestroyUndoState();
            undoState.position = hexBehaviourComponent.transform.position;
            undoState.containsShapeBlock = hexBehaviourComponent.containsShapeBlock;
            undoState.hexState = HexState.LoadFrom(hexBehaviourComponent);

            // Destroy orange hex.
            Destroy(hexBehaviourComponent.gameObject);

            // Signal success.
            musicDirector.GetComponent<MusicDirector>().PlayMouseOverSound();

            return undoState;
        }
    }

    private class MarkUndoState : UndoState
    {
        public List<HexMarking> hexMarkings = new List<HexMarking>();

        public RedoState Undo()
        {
            MarkRedoState redoState = new MarkRedoState();

            foreach (HexMarking hexMarking in hexMarkings) {
                HexBehaviour hexBehaviourComponent = instance.FindHexBehaviour(hexMarking.position);
                if (hexBehaviourComponent == null)
                {
                    continue;
                }

                HexMarking redoHexMarking = new HexMarking();
                redoHexMarking.position = hexMarking.position;
                redoHexMarking.hexState = HexState.LoadFrom(hexBehaviourComponent);
                redoState.hexMarkings.Add(redoHexMarking);

                hexMarking.hexState.SaveTo(hexBehaviourComponent);
            }
            
            return redoState;
        }
    }

    private class MarkRedoState : RedoState
    {
        public List<HexMarking> hexMarkings = new List<HexMarking>();

        public UndoState Redo()
        {
            MarkUndoState undoState = new MarkUndoState();

            foreach (HexMarking hexMarking in hexMarkings)
            {
                HexBehaviour hexBehaviourComponent = instance.FindHexBehaviour(hexMarking.position);
                if (hexBehaviourComponent == null)
                {
                    continue;
                }

                HexMarking undoHexMarking = new HexMarking();
                undoHexMarking.position = hexMarking.position;
                undoHexMarking.hexState = HexState.LoadFrom(hexBehaviourComponent);
                undoState.hexMarkings.Add(undoHexMarking);

                hexMarking.hexState.SaveTo(hexBehaviourComponent);
            }

            return undoState;
        }
    }

    public class HexMarking
    {
        public Vector3 position;
        public HexState hexState;
    }

    public class HexState
    {
        public enum HypotheticalState
        {
            Standard,
            HypotheticalBlack,
            HypotheticalBlue,
        }

        public HypotheticalState hypotheticalState;
        public bool hint;

        public static HexState LoadFrom(HexBehaviour hexBehaviourComponent)
        {
            HexState hexState = new HexState();
            hexState.hypotheticalState = HypotheticalState.Standard;
            hexState.hint = false;

            MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
            hexBehaviourComponent.thisRenderer.GetPropertyBlock(materialPropertyBlock);

            if (!materialPropertyBlock.isEmpty) { 
                Texture2D texture = materialPropertyBlock.GetTexture("_MainTex") as Texture2D;
                if (texture == instance.hintTexture)
                {
                    hexState.hint = true;
                }
                else if (texture == instance.hypotheticalBlackTexture)
                {
                    hexState.hypotheticalState = HypotheticalState.HypotheticalBlack;
                }
                else if (texture == instance.hypotheticalBlackHintTexture)
                {
                    hexState.hint = true;
                    hexState.hypotheticalState = HypotheticalState.HypotheticalBlack;
                }
                else if (texture == instance.hypotheticalBlueTexture)
                {
                    hexState.hypotheticalState = HypotheticalState.HypotheticalBlue;
                }
                else if (texture == instance.hypotheticalBlueHintTexture)
                {
                    hexState.hint = true;
                    hexState.hypotheticalState = HypotheticalState.HypotheticalBlue;
                }
            }

            return hexState;
        }

        public void SaveTo(HexBehaviour hexBehaviourComponent)
        {
            MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();

            if (hypotheticalState == HypotheticalState.Standard)
            {
                if (hint)
                {
                    materialPropertyBlock.SetTexture("_MainTex", instance.hintTexture);
                }
            }
            else if (hypotheticalState == HypotheticalState.HypotheticalBlack)
            {
                if (hint)
                {
                    materialPropertyBlock.SetTexture("_MainTex", instance.hypotheticalBlackHintTexture);
                }
                else
                {
                    materialPropertyBlock.SetTexture("_MainTex", instance.hypotheticalBlackTexture);
                }
            }
            else if (hypotheticalState == HypotheticalState.HypotheticalBlue)
            {
                if (hint)
                {
                    materialPropertyBlock.SetTexture("_MainTex", instance.hypotheticalBlueHintTexture);
                }
                else
                {
                    materialPropertyBlock.SetTexture("_MainTex", instance.hypotheticalBlueTexture);
                }
            }

            hexBehaviourComponent.thisRenderer.SetPropertyBlock(materialPropertyBlock);
        }
    }

    private void Awake()
    {
        instance = this;

        hintTexture = new Texture2D(0, 0);
        hintTexture.LoadImage(Resources.hint);
        hypotheticalBlueTexture = new Texture2D(0, 0);
        hypotheticalBlueTexture.LoadImage(Resources.hypothetical_blue);
        hypotheticalBlueHintTexture = new Texture2D(0, 0);
        hypotheticalBlueHintTexture.LoadImage(Resources.hypothetical_blue_hint);
        hypotheticalBlackTexture = new Texture2D(0, 0);
        hypotheticalBlackTexture.LoadImage(Resources.hypothetical_black);
        hypotheticalBlackHintTexture = new Texture2D(0, 0);
        hypotheticalBlackHintTexture.LoadImage(Resources.hypothetical_black_hint);

        harmony = Harmony.CreateAndPatchAll(typeof(Hooks), MyPluginInfo.PLUGIN_GUID);
    }

    private void OnDestroy()
    {
        harmony.UnpatchSelf();
    }

    private void OnLevelWasLoaded(int level)
    {
        ClearHistory();
    }

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool h = Input.GetKeyDown(KeyCode.H);
        bool x = Input.GetKeyDown(KeyCode.X);
        bool y = Input.GetKeyDown(KeyCode.Y);
        bool z = Input.GetKeyDown(KeyCode.Z);

        if (ctrl && !shift && z && undo.Count > 0)
        {
            Undo();
        }
        if (((ctrl && shift && z) || (ctrl && y)) && redo.Count > 0)
        {
            Redo();
        }
        if (ctrl && x)
        {
            ClearMarks();
        }
        if (ctrl && h)
        {
            Hint();
        }
    }

    private HexBehaviour FindHexBehaviour(Vector3 position)
    {
        GameObject hexGridOverlay = GameObject.Find("Hex Grid Overlay");
        if (hexGridOverlay == null)
        {
            return null;
        }
        foreach (Transform t in hexGridOverlay.transform)
        {
            if (t.position == position)
            {
                return t.gameObject.GetComponent<HexBehaviour>();
            }
        }
        return null;
    }

    private void ClearHistory()
    {
        undo.Clear();
        redo.Clear();
    }

    private void Undo()
    {
        GameObject hexScoring = GameObject.Find("Score Text");
        if (hexScoring == null || hexScoring.GetComponent<HexScoring>().levelIsComplete)
        {
            return;
        }

        UndoState undoState = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);

        RedoState redoState = undoState.Undo();
        if (redoState != null)
        {
            redo.Add(redoState);
        }
    }

    private void Redo()
    {
        GameObject hexScoring = GameObject.Find("Score Text");
        if (hexScoring == null || hexScoring.GetComponent<HexScoring>().levelIsComplete)
        {
            return;
        }

        RedoState redoState = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);

        UndoState undoState = redoState.Redo();
        if (undoState != null)
        {
            undo.Add(undoState);
        }
    }

    private void ClearMarks()
    {
        GameObject hexScoring = GameObject.Find("Score Text");
        GameObject hexGridOverlay = GameObject.Find("Hex Grid Overlay");
        if (hexScoring == null || hexGridOverlay == null || hexScoring.GetComponent<HexScoring>().levelIsComplete)
        {
            return;
        }

        MarkUndoState undoState = new MarkUndoState();
        foreach (Transform t in hexGridOverlay.transform)
        {
            HexBehaviour hexBehaviourComponent = t.gameObject.GetComponent<HexBehaviour>();
            HexState hexState = HexState.LoadFrom(hexBehaviourComponent);
            if (hexState.hypotheticalState == HexState.HypotheticalState.Standard)
            {
                continue;
            }

            HexMarking hexMarking = new HexMarking();
            hexMarking.position = t.position;
            hexMarking.hexState = HexState.LoadFrom(hexBehaviourComponent);
            undoState.hexMarkings.Add(hexMarking);

            hexState.hypotheticalState = HexState.HypotheticalState.Standard;
            hexState.SaveTo(hexBehaviourComponent);
        }

        if (undoState.hexMarkings.Count > 0)
        {
            redo.Clear();
            undo.Add(undoState);
        }
    }

    private void Hint()
    {
        GameObject hexScoring = GameObject.Find("Score Text");
        GameObject hexGridOverlay = GameObject.Find("Hex Grid Overlay");
        if (hexScoring == null || hexGridOverlay == null || hexScoring.GetComponent<HexScoring>().levelIsComplete)
        {
            return;
        }

        MarkUndoState undoState = new MarkUndoState();
        foreach (Transform t in hexGridOverlay.transform)
        {
            HexBehaviour hexBehaviourComponent = t.GetComponent<HexBehaviour>();
            HexState hexState = HexState.LoadFrom(hexBehaviourComponent);
            if (hexState.hint)
            {
                HexMarking hexMarking = new HexMarking();
                hexMarking.position = t.position;
                hexMarking.hexState = HexState.LoadFrom(hexBehaviourComponent);
                undoState.hexMarkings.Add(hexMarking);

                hexState.hint = false;
                hexState.SaveTo(hexBehaviourComponent);
            }
        }
        if (undoState.hexMarkings.Count > 0)
        {
            redo.Clear();
            undo.Add(undoState);
            return;
        }

        Solver.maxDepth = 2;
        Solver solver = new Solver();
        solver.LoadLevelDataIntoSolver();

        FieldInfo setsField = typeof(Solver).GetField("sets", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo countValidSolutionsInfo = typeof(Set).GetMethod("CountValidSolutions", BindingFlags.NonPublic | BindingFlags.Instance);

        List<Set> sets = setsField.GetValue(solver) as List<Set>;
        foreach (Set set in sets)
        {
            if (!set.isVisible || set.permutationComplexityIndex > 40)
            {
                continue;
            }

            int numOrange = 0;
            int numBlueRemaining = set.bluesInSet;
            foreach (Cell cell in set.cells)
            {
                cell.solutionAppearances = 0;
                if (cell.cellState == CellStateEnum.CellState.Orange)
                {
                    numOrange++;
                }
                else if (cell.cellState == CellStateEnum.CellState.Blue)
                {
                    numBlueRemaining--;
                }
            }

            int solutionsFound = 0;
            if (numOrange > 0 && numBlueRemaining > 0)
            {
                object[] countValidSolutionsParameters = [set.cells, numBlueRemaining, solutionsFound, 0, 0];
                countValidSolutionsInfo.Invoke(set, countValidSolutionsParameters);
                numBlueRemaining = (int)countValidSolutionsParameters[1];
                solutionsFound = (int)countValidSolutionsParameters[2];
            }

            foreach (Cell cell in set.cells)
            {
                if (cell.cellState == CellStateEnum.CellState.Orange)
                {
                    if (cell.solutionAppearances == 0 || cell.solutionAppearances == solutionsFound)
                    {
                        HexState hexState = HexState.LoadFrom(cell.objectRef);
                        if (!hexState.hint)
                        {
                            HexMarking hexMarking = new HexMarking();
                            hexMarking.position = cell.objectRef.transform.position;
                            hexMarking.hexState = HexState.LoadFrom(cell.objectRef);
                            undoState.hexMarkings.Add(hexMarking);

                            hexState.hint = true;
                            hexState.SaveTo(cell.objectRef);
                        }
                    }
                }
            }
        }

        if (undoState.hexMarkings.Count > 0)
        {
            redo.Clear();
            undo.Add(undoState);
        }
    }

    private void OnDestroyHex(HexBehaviour hexBehaviourComponent)
    {
        DestroyUndoState undoState = new DestroyUndoState();
        undoState.position = hexBehaviourComponent.transform.position;
        undoState.containsShapeBlock = hexBehaviourComponent.containsShapeBlock;
        undoState.hexState = HexState.LoadFrom(hexBehaviourComponent);

        redo.Clear();
        undo.Add(undoState);
    }

    private void OnMarkHex(HexBehaviour hexBehaviourComponent)
    {
        MarkUndoState undoState = new MarkUndoState();
        HexMarking hexMarking = new HexMarking();
        hexMarking.position = hexBehaviourComponent.transform.position;
        hexMarking.hexState = HexState.LoadFrom(hexBehaviourComponent);
        undoState.hexMarkings.Add(hexMarking);

        HexState hexState = HexState.LoadFrom(hexBehaviourComponent);
        switch (hexState.hypotheticalState)
        {
            case HexState.HypotheticalState.Standard:
                hexState.hypotheticalState = HexState.HypotheticalState.HypotheticalBlue;
                break;
            case HexState.HypotheticalState.HypotheticalBlue:
                hexState.hypotheticalState = HexState.HypotheticalState.HypotheticalBlack;
                break;
            case HexState.HypotheticalState.HypotheticalBlack:
                hexState.hypotheticalState = HexState.HypotheticalState.Standard;
                break;
        }
        hexState.SaveTo(hexBehaviourComponent);

        redo.Clear();
        undo.Add(undoState);
    }

    private void OnMarkHexBlue(HexBehaviour hexBehaviourComponent)
    {
        MarkUndoState undoState = new MarkUndoState();
        HexMarking hexMarking = new HexMarking();
        hexMarking.position = hexBehaviourComponent.transform.position;
        hexMarking.hexState = HexState.LoadFrom(hexBehaviourComponent);
        undoState.hexMarkings.Add(hexMarking);

        HexState hexState = HexState.LoadFrom(hexBehaviourComponent);
        if (hexState.hypotheticalState == HexState.HypotheticalState.HypotheticalBlue)
        {
            hexState.hypotheticalState = HexState.HypotheticalState.Standard;
        }
        else
        {
            hexState.hypotheticalState = HexState.HypotheticalState.HypotheticalBlue;
        }
        hexState.SaveTo(hexBehaviourComponent);

        redo.Clear();
        undo.Add(undoState);
    }

    private void OnMarkHexBlack(HexBehaviour hexBehaviourComponent)
    {
        MarkUndoState undoState = new MarkUndoState();
        HexMarking hexMarking = new HexMarking();
        hexMarking.position = hexBehaviourComponent.transform.position;
        hexMarking.hexState = HexState.LoadFrom(hexBehaviourComponent);
        undoState.hexMarkings.Add(hexMarking);

        HexState hexState = HexState.LoadFrom(hexBehaviourComponent);
        if (hexState.hypotheticalState == HexState.HypotheticalState.HypotheticalBlack)
        {
            hexState.hypotheticalState = HexState.HypotheticalState.Standard;
        }
        else
        {
            hexState.hypotheticalState = HexState.HypotheticalState.HypotheticalBlack;
        }
        hexState.SaveTo(hexBehaviourComponent);

        redo.Clear();
        undo.Add(undoState);
    }

    private static class Hooks
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HexBehaviour), "OnMouseOver")]
        private static bool HexBehavior_OnMouseOver_Hook(HexBehaviour __instance)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift && Input.GetMouseButtonDown(0))
            {
                instance.OnMarkHexBlue(__instance);
                return false;
            }
            if (shift && Input.GetMouseButtonDown(1))
            {
                instance.OnMarkHexBlack(__instance);
                return false;
            }
            if (Input.GetMouseButtonDown(2))
            {
                instance.OnMarkHex(__instance);
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HexBehaviour), "DestroyClick")]
        private static bool HexBehavior_DestroyClick_Hook(HexBehaviour __instance)
        {
            if (!__instance.containsShapeBlock)
            {
                instance.OnDestroyHex(__instance);
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HexBehaviour), "HighlightClick")]
        private static bool HexBehavior_HighlightClick_Hook(HexBehaviour __instance)
        {
            if (__instance.containsShapeBlock)
            {
                instance.OnDestroyHex(__instance);
            }
            return true;
        }
    }
}
