using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;

public class ReflectionScript : MonoBehaviour
{
    // Public variables are set on the unity side
    public KMAudio audio;
    public KMBombInfo bomb;

    public KMSelectable[] cells;
    public Texture[] icons;
    public KMSelectable[] edges;
    public TextMesh[] edgeNums;
    public TextMesh[] edgeColorblinds;
    public GameObject[] laserVerticals;
    public GameObject[] laserHorizontals;
    public KMSelectable submit;

    public AudioClip[] sounds;

    // like global constant
    private static readonly int BOARD_SIZE = 5;
    private static readonly Color[] EDGE_COLORS = {
        new Color((float)0x99/0xff, (float)0xcc/0xff, (float)0x99/0xff, (float)0xcc/0xff), // green
        new Color((float)0xff/0xff, (float)0xff/0xff, (float)0xab/0xff, (float)0xcc/0xff), // yellow
        new Color((float)0x80/0xff, (float)0x80/0xff, (float)0x80/0xff, (float)0xcc/0xff), // ash
        new Color((float)0xcc/0xff, (float)0x99/0xff, (float)0x99/0xff, (float)0xcc/0xff)  // red
    };

    // like global variable
    static int sameModuleCounter = 0;
    int moduleId;
    private bool moduleSolved = false;

    private int[,] cellIconMap = new int[BOARD_SIZE, BOARD_SIZE];
    private int[,] answerBoard = new int[BOARD_SIZE, BOARD_SIZE];

    private int[,,] answerEdges = new int[4, BOARD_SIZE, 2];

    private bool[,] isPassedVerticals   = new bool[BOARD_SIZE + 1, BOARD_SIZE    ];
    private bool[,] isPassedHorizontals = new bool[BOARD_SIZE    , BOARD_SIZE + 1];

    private int[,] warpPositions = { { 0, 0 }, { 0, 0 } };

    private IEnumerator playNoise;
    private KMAudio.KMAudioRef noisePlayer;

    private void Awake()
    {
        moduleId = ++sameModuleCounter;

        for (int i = 0; i < cells.Length; i++)
        {
            int cellIndex = i;
            cells[i].OnInteract += delegate () { PressCell(cellIndex); return false; };
        }

        for (int i = 0; i < edges.Length; i++)
        {
            int edgeIndex = i;
            edges[i].OnInteract += delegate () { PressEdge(edgeIndex); return false; };
            edges[i].OnInteractEnded += delegate () { ReleaseEdge(edgeIndex); };
        }

        submit.OnInteract += delegate () { PressSubmit(); return false; };
    }

    // Use this for initialization
    void Start()
    {
        // set random seed
        TimeSpan currentTime = DateTime.Now.TimeOfDay;
        UnityEngine.Random.InitState((int)(currentTime.Ticks + moduleId));

        // initialize
        GeneratePuzzle();

        // Debug Message
        DebugLog("Answer grid: " + MakeStringFromBoard(answerBoard));

        FillAnswer();
        UpdateAnswerEdges();

        // Debug Message
        DebugLog("Answer edge: " + MakeStringFromEdges(answerEdges));

        ResetBoard();
        ResetEdges();
    }

    // Update is called once per frame
    // use to get constantly updating info
    // ex.) solves, strikes
    /*
    void Update () {
		
	}
    */

    private void PressCell(int cellIndex)
    {
        if (moduleSolved) return;

        int indexY = cellIndex / BOARD_SIZE;
        int indexX = cellIndex % BOARD_SIZE;

        if (cellIconMap[indexY, indexX] == 10)
        {
            float bombTimeCurrent = bomb.GetTime();
            int bombTimeCurrentSecLastDigit = (int)bombTimeCurrent % 10;

            UpdateCellView(cellIndex, bombTimeCurrentSecLastDigit);
        }
        else if (cellIconMap[indexY, indexX] != 11) // 11: warp
        {
            UpdateCellView(cellIndex, 10);
        }
    }

    private void PressEdge(int edgeIndex)
    {
        playNoise = PlayNoise();
        StartCoroutine(playNoise);

        int[] edgeStatus = CalcEdgeStatus(edgeIndex);

        UpdateEdgeView(edgeIndex, edgeStatus[0], edgeStatus[1]);

        DrawLaserPath();
    }

    private void ReleaseEdge(int edgeIndex)
    {
        noisePlayer.StopSound();
        StopCoroutine(playNoise);
        playNoise = null;

        ResetLaserPath();
        DrawLaserPath();

        ResetEdges();
    }

    private void PressSubmit()
    {
        if (moduleSolved) return;

        // Debug Message
        DebugLog("Submission: " + MakeStringFromBoard(cellIconMap));

        bool isCorrect = true;
        List<int> incorrectEdges = new List<int> { };

        for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            int[] edgeStatus = CalcEdgeStatus(edgeIndex);

            int edgeSide = edgeIndex / BOARD_SIZE;
            int indexOnSide = edgeIndex % BOARD_SIZE;

            if (answerEdges[edgeSide, indexOnSide, 0] != edgeStatus[0] ||
                answerEdges[edgeSide, indexOnSide, 1] != edgeStatus[1])
            {
                incorrectEdges.Add(edgeIndex);
                isCorrect = false;

                // Debug Message
                string debugIncorrect = "Your submission of edge ";

                switch (edgeSide)
                {
                    case 0: debugIncorrect += "U" + (char)(indexOnSide + 'A'); break;
                    case 1: debugIncorrect += "D" + (char)(indexOnSide + 'A'); break;
                    case 2: debugIncorrect += "L" + (indexOnSide + 1).ToString(); break;
                    case 3: debugIncorrect += "R" + (indexOnSide + 1).ToString(); break;
                }

                debugIncorrect += " is ";

                switch (edgeStatus[0])
                {
                    case 0: debugIncorrect += "green";  break;
                    case 1: debugIncorrect += "yellow"; break;
                    case 2: debugIncorrect += "ash";    break;
                }

                debugIncorrect += " " + edgeStatus[1].ToString() + ", but the answer is ";

                switch (answerEdges[edgeSide, indexOnSide, 0])
                {
                    case 0: debugIncorrect += "green";  break;
                    case 1: debugIncorrect += "yellow"; break;
                    case 2: debugIncorrect += "ash";    break;
                }

                debugIncorrect += " " + answerEdges[edgeSide, indexOnSide, 1].ToString() + ". Incorrect!";

                DebugLog(debugIncorrect);
            }
        }

        if (isCorrect)
        {
            // Debug Message
            DebugLog("Your submission is correct! Module solved.");

            GetComponent<KMAudio>().PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
            GetComponent<KMBombModule>().HandlePass();
            moduleSolved = true;
        }
        else
        {
            GetComponent<KMBombModule>().HandleStrike();
            StartCoroutine(ShowIncorrectEdge(incorrectEdges));
        }
    }

    private void DebugLog<Type>(Type msg)
    {
        Debug.LogFormat("[Reflection #{0}] {1}", moduleId, msg);
    }

    private void GeneratePuzzle()
    {
        // preparing bomb data
        bool shouldFlip = false;

        if (bomb.GetSerialNumberNumbers().Count() > 2) shouldFlip = true;

        string serialNumber = bomb.GetSerialNumber();
        string VOWELS = "AEIOU";
        bool hasVowel = false;

        foreach (char vowel in VOWELS) if (serialNumber.IndexOf(vowel) > -1) hasVowel = true;

        int DBatteryHolderNum = bomb.GetBatteryHolderCount(1);
        int AABatteryHolderNum = bomb.GetBatteryHolderCount(2);

        int onIndicatorsNum = bomb.GetOnIndicators().Count();
        int offIndicatorsNum = bomb.GetOffIndicators().Count();
        bool hasLitBob = bomb.IsIndicatorOn("BOB");

        bool hasDVI = bomb.IsPortPresent("DVI");
        bool hasParallel = bomb.IsPortPresent("Parallel");
        bool hasPS2 = bomb.IsPortPresent("PS2");
        bool hasRJ45 = bomb.IsPortPresent("RJ45");
        bool hasSerial = bomb.IsPortPresent("Serial");
        bool hasStereoRCA = bomb.IsPortPresent("StereoRCA");
        int uniquePortNum = bomb.CountUniquePorts();
        int portPlateNum = bomb.GetPortPlateCount();

        // change iconNums
        int[] iconNums = new int[] { 3, 3, 1, 1, 0, 1, 0, 0, 0, 0 };

        if (hasVowel) iconNums[6]++;
        else iconNums[7]++;

        for (int i = 0; i < DBatteryHolderNum; i++) iconNums[0]++;
        for (int i = 0; i < AABatteryHolderNum; i++) iconNums[1]++;

        switch ((DBatteryHolderNum + 2 * AABatteryHolderNum) % 3)
        {
            case 1: iconNums[2]++; break;
            case 2: iconNums[3]++; break;
            default: iconNums[4]++; break;
        }

        if (onIndicatorsNum > offIndicatorsNum) iconNums[6]++;
        else if (onIndicatorsNum < offIndicatorsNum) iconNums[7]++;
        else iconNums[4]++;

        if (hasLitBob) for (int i = 0; i < 8; i++) iconNums[i]++;

        if (hasDVI) iconNums[2]++;
        if (hasPS2) iconNums[3]++;
        if (hasSerial) iconNums[4]++;
        if (hasParallel) iconNums[5]++;
        if (hasRJ45) iconNums[6]++;
        if (hasStereoRCA) iconNums[7]++;

        // adjust iconNums
        int decreaseIconNum = 0;
        decreaseIconNum += DBatteryHolderNum + AABatteryHolderNum;
        decreaseIconNum += Math.Min(uniquePortNum, portPlateNum);

        int iconNumsSum = 0;

        for (int i = 0; i < iconNums.Count(); i++) iconNumsSum += iconNums[i];

        int MAX_ICON_NUMS_SUM = 18;

        if (decreaseIconNum < iconNumsSum - MAX_ICON_NUMS_SUM) decreaseIconNum = iconNumsSum - MAX_ICON_NUMS_SUM;

        for (int i = 0; i < decreaseIconNum; i++)
        {
            if (iconNums[i % 8] > 0) iconNums[i % 8]--;
            else decreaseIconNum++; // increase limit instead of decreasing
        }

        if (shouldFlip) // bl -> br, tr -> tl
        {
            iconNums[8] = iconNums[6];
            iconNums[9] = iconNums[7];
            iconNums[6] = 0;
            iconNums[7] = 0;
        }

        // filling order
        int[] cellOrder = Enumerable.Range(0, cells.Length).ToArray();
        cellOrder = cellOrder.OrderBy(x => UnityEngine.Random.Range(int.MinValue, int.MaxValue)).ToArray();

        Queue<int> cellQueue = new Queue<int>(cellOrder);

        // make answerBoard
        for (int i = 0; i < iconNums.Length; i++)
        {
            for (int j = 0; j < iconNums[i]; j++)
            {
                int cellIndex = cellQueue.Dequeue();
                int cellIndexY = cellIndex / BOARD_SIZE;
                int cellIndexX = cellIndex % BOARD_SIZE;

                answerBoard[cellIndexY, cellIndexX] = i;
            }
        }

        // 11: warp
        for (int i = 0; i < 2; i++)
        {
            int cellIndex = cellQueue.Dequeue();
            int cellIndexY = cellIndex / BOARD_SIZE;
            int cellIndexX = cellIndex % BOARD_SIZE;

            answerBoard[cellIndexY, cellIndexX] = 11;

            warpPositions[i, 0] = cellIndexY + 1;
            warpPositions[i, 1] = cellIndexX + 1;
        }

        // 10: blank
        while (cellQueue.Any())
        {
            int cellIndex = cellQueue.Dequeue();
            int cellIndexY = cellIndex / BOARD_SIZE;
            int cellIndexX = cellIndex % BOARD_SIZE;

            answerBoard[cellIndexY, cellIndexX] = 10;
        }

        DebugSeedCode(answerBoard, shouldFlip);
    }

    private void FillAnswer()
    {
        for (int i = 0; i < cells.Length; i++)
        {
            int indexY = i / BOARD_SIZE;
            int indexX = i % BOARD_SIZE;

            UpdateCellView(i, answerBoard[indexY, indexX]);
        }
    }

    private void UpdateAnswerEdges()
    {
        for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            int[] edgeStatus = CalcEdgeStatus(edgeIndex);

            int edgeSide = edgeIndex / BOARD_SIZE;
            int indexOnSide = edgeIndex % BOARD_SIZE;

            answerEdges[edgeSide, indexOnSide, 0] = edgeStatus[0];
            answerEdges[edgeSide, indexOnSide, 1] = edgeStatus[1];
        }
    }

    private int[] CalcEdgeStatus(int edgeIndex)
    {
        int edgeSide = edgeIndex / BOARD_SIZE;
        int indexOnSide = edgeIndex % BOARD_SIZE;

        int[] edgeStatus = new int[] { };

        switch (edgeSide)
        {
            case 0: edgeStatus = ChaseLaser(              1, indexOnSide + 1, 0, 0); break; // up
            case 1: edgeStatus = ChaseLaser( BOARD_SIZE    , indexOnSide + 1, 1, 0); break; // down
            case 2: edgeStatus = ChaseLaser(indexOnSide + 1,               1, 2, 0); break; // left
            case 3: edgeStatus = ChaseLaser(indexOnSide + 1,  BOARD_SIZE    , 3, 0); break; // right
        }

        return edgeStatus;
    }

    private int[] ChaseLaser(int y, int x, int comingDirection, int distance)
    {
        // initialize
        if (distance == 0)
        {
            ResetLaserPath();
        }

        // update passed path
        switch (comingDirection)
        {
            case 0: isPassedVerticals  [y - 1, x - 1] = true; break;
            case 1: isPassedVerticals  [y    , x - 1] = true; break;
            case 2: isPassedHorizontals[y - 1, x - 1] = true; break;
            case 3: isPassedHorizontals[y - 1, x    ] = true; break;
        }

        // direction ... 0: up, 1: down, 2: left, 3: right
        int[,] REFLECTED_DIRECTIONS = {
            { 3, 2, 1, 0 },
            { 2, 3, 0, 1 },
            { 0, 1, 3, 2 },
            { 1, 0, 2, 3 },
            { 1, 0, 3, 2 },
            {-1,-1,-1,-1 },
            { 2, 0, 3, 1 },
            { 1, 3, 0, 2 },
            { 3, 0, 1, 2 },
            { 1, 2, 3, 0 },
            { 0, 1, 2, 3 }
        };

        Func<int, int[]> dirToNextCellPos = (dir) =>
        {
            int[] returnStatus = new int[] { 0, 0 };
            switch (dir)
            {
                case 0: returnStatus[0] =  1; break;
                case 1: returnStatus[0] = -1; break;
                case 2: returnStatus[1] =  1; break;
                case 3: returnStatus[1] = -1; break;
            }
            return returnStatus;
        };

        // return[0] ... 0: green, 1: yellow, 2: ash
             if (y == 0             ) return new int[] { 0, distance };
        else if (y == BOARD_SIZE + 1) return new int[] { 0, distance };
        else if (x == 0             ) return new int[] { 0, distance };
        else if (x == BOARD_SIZE + 1) return new int[] { 0, distance };
        else
        {
            switch (cellIconMap[y - 1, x - 1])
            {
                case 5: // blackhole
                    return new int[] { 2, ++distance };

                case 11: // warp
                    // nextPos
                    int warpedY, warpedX;

                    if (y == warpPositions[0, 0] && x == warpPositions[0, 1])
                    {
                        warpedY = warpPositions[1, 0];
                        warpedX = warpPositions[1, 1];
                    }
                    else
                    {
                        warpedY = warpPositions[0, 0];
                        warpedX = warpPositions[0, 1];
                    }

                    ++distance;

                    // afterWarped
                    int[] afterWarpedPos = dirToNextCellPos(comingDirection);
                    int afterWarpednextY = warpedY + afterWarpedPos[0];
                    int afterWarpednextX = warpedX + afterWarpedPos[1];

                    return ChaseLaser(afterWarpednextY, afterWarpednextX, comingDirection, ++distance);

                default:
                    int nextDirection = REFLECTED_DIRECTIONS[cellIconMap[y - 1, x - 1], comingDirection];

                    // HACK: 0+1=1, 2+3=5 => (up & down) OR (left & right)
                    if ((nextDirection + comingDirection) % 4 == 1) return new int[] { 1, 2 * distance + 1 };

                    int[] nextPos = dirToNextCellPos(nextDirection);
                    int nextY = y + nextPos[0];
                    int nextX = x + nextPos[1];

                    return ChaseLaser(nextY, nextX, nextDirection, ++distance);
            }
        }
    }

    private void ResetBoard()
    {
        for (int i = 0; i < cells.Length; i++)
        {
            int indexY = i / BOARD_SIZE;
            int indexX = i % BOARD_SIZE;

            if (answerBoard[indexY, indexX] == 11) UpdateCellView(i, 11);
            else UpdateCellView(i, 10);
        }
    }

    private void ResetEdges()
    {
        for (int i = 0; i < edges.Length; i++)
        {
            int edgeSide = i / BOARD_SIZE;
            int indexOnSide = i % BOARD_SIZE;

            UpdateEdgeView(i, answerEdges[edgeSide, indexOnSide, 0], answerEdges[edgeSide, indexOnSide, 1]);
        }
    }

    private void UpdateCellView(int cellIndex, int iconIndex)
    {
        int cellIndexY = cellIndex / BOARD_SIZE;
        int cellIndexX = cellIndex % BOARD_SIZE;

        cells[cellIndex].GetComponent<MeshRenderer>().material.mainTexture = icons[iconIndex];
        cellIconMap[cellIndexY, cellIndexX] = iconIndex;
    }

    private void UpdateEdgeView(int edgeIndex, int edgeColorIndex, int edgeNum)
    {
        edges[edgeIndex].GetComponent<MeshRenderer>().material.color = EDGE_COLORS[edgeColorIndex];

        char colorChar = 'W'; // not use

        switch (edgeColorIndex)
        {
            case 0: colorChar = 'G'; break;
            case 1: colorChar = 'Y'; break;
            case 2: colorChar = 'A'; break;
            case 3: colorChar = 'R'; break;
        }

        if (GetComponent<KMColorblindMode>().ColorblindModeActive) edgeColorblinds[edgeIndex].text = colorChar.ToString();
        else edgeColorblinds[edgeIndex].text = "";

        edgeNums[edgeIndex].text = edgeNum.ToString();
    }

    private IEnumerator ShowIncorrectEdge(List<int> incorrectEdges)
    {
        foreach (int edgeIndex in incorrectEdges)
        {
            int edgeSide = edgeIndex / BOARD_SIZE;
            int indexOnSide = edgeIndex % BOARD_SIZE;

            UpdateEdgeView(edgeIndex, 3, answerEdges[edgeSide, indexOnSide, 1]); // 3: red
        }

        yield return new WaitForSeconds(1.0f);

        ResetEdges();
    }

    private void DrawLaserPath()
    {
        int indexY, indexX;

        for (int i = 0; i < laserVerticals.Length; i++)
        {
            indexY = i / BOARD_SIZE;
            indexX = i % BOARD_SIZE;

            laserVerticals[i].GetComponent<MeshRenderer>().enabled = isPassedVerticals[indexY, indexX];
        }

        for (int i = 0; i < laserHorizontals.Length; i++)
        {
            indexY = i / (BOARD_SIZE + 1);
            indexX = i % (BOARD_SIZE + 1);

            laserHorizontals[i].GetComponent<MeshRenderer>().enabled = isPassedHorizontals[indexY, indexX];
        }
    }

    private void ResetLaserPath()
    {
        isPassedHorizontals = new bool[BOARD_SIZE, BOARD_SIZE + 1];
        isPassedVerticals = new bool[BOARD_SIZE + 1, BOARD_SIZE];
    }

    private IEnumerator PlayNoise()
    {
        noisePlayer = audio.PlaySoundAtTransformWithRef(sounds[0].name, transform);

        yield return new WaitForSeconds(sounds[0].length);

        StartCoroutine(playNoise);
    }

    private void DebugSeedCode(int[,] answerBoard, bool shouldFlip)
    {
        string puzzleSeed = "";

        int[] MAGIC_NUMBERS = { 63, 33, 35, 167, 38, 42, 43, 45, 37, 95, 61, 64, 247, 36, 163, 58, 60, 62, 162, 172, 46, 44, 165, 126, 47, 92, 91, 93, 94, 40, 41, 123, 125, 59, 124, 169, 174, 164 };

        for (int y = 0; y < BOARD_SIZE; y++)
        {
            int index = 0;
            for (int x = 0; x < BOARD_SIZE + BOARD_SIZE % 2; x++)
            {
                int conversion;
                if (shouldFlip)
                {
                    conversion = BOARD_SIZE - (x + 2 - x % 2 * 2) > -1 ? answerBoard[y, BOARD_SIZE - (x + 2 - x % 2 * 2)] : 10;
                    switch (conversion)
                    {
                        case 0: conversion = 2; break;
                        case 10: conversion = 0; break;
                        case 11: conversion = 7; break;
                        default: if (conversion > 1 && conversion < 8) conversion++; break;
                    }
                }
                else
                {
                    conversion = x + 1 - x % 2 * 2 < BOARD_SIZE ? answerBoard[y, x + 1 - x % 2 * 2] : 10;
                    switch (conversion)
                    {
                        case 10: conversion = 0; break;
                        case 11: conversion = 7; break;
                        default:
                            conversion++;
                            if (conversion > 6) conversion++;
                            break;
                    }
                }

                if (x % 2 == 0)
                {
                    index += conversion;
                }
                else
                {
                    if (conversion != 0) index = (index + 1) * 9;
                    index += conversion;

                    if (index < 26) index += 65;
                    else if (index < 52) index += 71;
                    else if (index < 62) index -= 4;
                    else if (index == 81) index = MAGIC_NUMBERS[index - 62] + (1 << 13);
                    else index = MAGIC_NUMBERS[index - 62];

                    puzzleSeed += (char)index;

                    index = 0;
                }
            }
        }

        DebugLog("Seed code: " + puzzleSeed);
    }

    private string MakeStringFromBoard(int[,] board)
    {
        string boardToString = "{ ";

        for (int y = 0; y < BOARD_SIZE; y++)
        {
            if (y != 0) boardToString += ", { ";
            else boardToString += "{ ";

            for (int x = 0; x < BOARD_SIZE; x++)
            {
                if (x != 0) boardToString += ", ";
                boardToString += board[y, x];
            }

            boardToString += " }";
        }

        boardToString += " };";

        return boardToString;
    }

    private string MakeStringFromEdges(int[,,] edges)
    {
        string edgeToString = "{ ";

        for (int edgeSide = 0; edgeSide < 4; edgeSide++)
        {
            if (edgeSide != 0) edgeToString += ", { ";
            else edgeToString += "{ ";

            for (int indexOnSide = 0; indexOnSide < BOARD_SIZE; indexOnSide++)
            {
                if (indexOnSide != 0) edgeToString += ", ";

                edgeToString += "{ ";

                switch (edges[edgeSide, indexOnSide, 0])
                {
                    case 0: edgeToString += "green";  break;
                    case 1: edgeToString += "yellow"; break;
                    case 2: edgeToString += "ash";    break;
                }

                edgeToString += ", " + edges[edgeSide, indexOnSide, 1] + " }";
            }

            edgeToString += " }";
        }

        edgeToString += " };";

        return edgeToString;
    }
}
