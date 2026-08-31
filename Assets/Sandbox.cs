using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System 쓰려면 필요
using UnityEngine.UI; // Canvas, Text 쓰려면 필요

public class Sandbox : MonoBehaviour
{
    public GameObject squarePrefab; // 블록 하나로 쓸 흰 정사각형 프리팹 (인스펙터에서 연결)
    GameObject activePiece; // 지금 떨어지고 있는 피스

    // 보드/낙하 관련 상태
    GameObject[,] boardCells = new GameObject[10, 20]; // null이면 빈 칸, 아니면 그 칸의 고정 블록
    float fallTimer = 0f;
    float fallInterval = 1f; // 초기 낙하 간격 (초)
    float minFallInterval = 0.15f; // 이보다 빨라지지는 않음
    float speedUpRate = 0.01f; // 플레이 1초당 낙하 간격이 줄어드는 양
    float fastFallInterval = 0.05f; // 아래키를 누르고 있을 때 낙하 간격
    bool isGameOver = false;

    float elapsedTime = 0f; // 플레이 시간(초)

    // 모양/점수 관련 상태
    ShapeData[] allShapes; // 테트로미노 7종
    ShapeData nextShape; // 미리보기로 보여주는 다음 모양
    GameObject previewPiece;
    int score = 0;

    // UI 텍스트들
    GameObject canvasObject;
    Text scoreText;
    Text timeText;
    Text gameOverText;
    Text finalScoreText;
    Text restartHintText;

    // 그리드 좌표(col, row)를 실제 월드 좌표로 변환
    Vector3 GridToWorldPosition(int col, int row)
    {
        return new Vector3(col, row, 0);
    }

    // 보드 양옆에 벽 2개 생성
    void CreateWalls()
    {
        CreateWall(new Vector3(-0.55f, 9.5f, 0));
        CreateWall(new Vector3(9.55f, 9.5f, 0));
    }

    // 지정된 위치에 얇고 긴 벽 하나 생성 (squarePrefab을 스케일만 다르게 재사용)
    void CreateWall(Vector3 position)
    {
        GameObject wall = Instantiate(squarePrefab, position, Quaternion.identity);
        wall.transform.localScale = new Vector3(0.1f, 20f, 1f); // 얇고 긴 기둥 모양으로 스케일
        wall.GetComponent<SpriteRenderer>().color = new Color(0.35f, 0.35f, 0.4f); // 블록과 구분되는 어두운 회색
    }

    // 점수/시간/게임오버 관련 UI 텍스트들을 만들어서 배치
    void CreateUI()
    {
        canvasObject = new GameObject("Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>();

        scoreText = CreateText("ScoreText", true, new Vector2(20, -20));
        scoreText.text = "점수: 0";
        scoreText.fontSize = 22;
        scoreText.color = Color.yellow;

        timeText = CreateText("TimeText", true, new Vector2(20, -60));
        timeText.text = "시간: 00:00";
        timeText.fontSize = 18;
        timeText.color = Color.cyan;

        gameOverText = CreateText("GameOverText", false, new Vector2(0, 80));
        gameOverText.fontSize = 60;
        gameOverText.color = Color.red;
        gameOverText.text = ""; // 게임오버 전엔 빈 문자열이라 안 보임

        finalScoreText = CreateText("FinalScoreText", false, new Vector2(0, 0));
        finalScoreText.fontSize = 32;
        finalScoreText.color = Color.yellow;
        finalScoreText.text = "";

        restartHintText = CreateText("RestartHintText", false, new Vector2(0, -60));
        restartHintText.fontSize = 20;
        restartHintText.color = Color.white;
        restartHintText.text = "";
    }

    // Canvas 아래에 텍스트 UI 오브젝트 하나를 만들어서 반환 (왼쪽 위 고정 or 정중앙 고정)
    Text CreateText(string name, bool anchorTopLeft, Vector2 anchoredPosition)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(canvasObject.transform);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = Color.white;

        RectTransform rect = text.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500, 100);

        if (anchorTopLeft)
        {
            text.alignment = TextAnchor.UpperLeft;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
        }
        else
        {
            text.alignment = TextAnchor.MiddleCenter;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        rect.anchoredPosition = anchoredPosition;
        return text;
    }

    // 현재 피스를 90도 회전 (clockwise: true=시계방향, false=반시계방향). 벽/블록에 막히면 회전 취소
    void Rotate(bool clockwise)
    {
        List<Transform> children = new List<Transform>();
        List<Vector3> newLocalPositions = new List<Vector3>();

        foreach (Transform child in activePiece.transform)
        {
            children.Add(child);

            float x = child.localPosition.x;
            float y = child.localPosition.y;

            if (clockwise)
            {
                newLocalPositions.Add(new Vector3(y, -x, 0)); // 시계방향 90도
            }
            else
            {
                newLocalPositions.Add(new Vector3(-y, x, 0)); // 반시계방향 90도
            }
        }

        for (int i = 0; i < children.Count; i++)
        {
            Vector3 worldPos = activePiece.transform.position + newLocalPositions[i];
            int col = Mathf.RoundToInt(worldPos.x);
            int row = Mathf.RoundToInt(worldPos.y);

            if (col < 0 || col > 9) return;             // 회전했을 때 벽 밖이면 회전 취소
            if (row < 0 || row > 19) return;
            if (boardCells[col, row] != null) return;   // 다른 고정 블록과 겹치면 회전 취소
        }

        for (int i = 0; i < children.Count; i++)
        {
            children[i].localPosition = newLocalPositions[i];
        }
    }

    // 현재 피스가 offset만큼 이동해도 되는지(벽/바닥/다른 블록에 안 걸리는지) 검사
    bool CanMove(Vector3 offset)
    {
        foreach (Transform child in activePiece.transform)
        {
            Vector3 newPos = child.position + offset;
            int col = Mathf.RoundToInt(newPos.x);
            int row = Mathf.RoundToInt(newPos.y);

            if (col < 0 || col > 9) return false; // 보드 폭: 0~9 (10칸)
            if (row < 0 || row > 19) return false; // 바닥 아래, 천장 위로는 못 감
            if (boardCells[col, row] != null) return false; // 이미 고정된 블록이 있는 칸
        }
        return true;
    }

    // 현재 피스를 보드에 고정시키고, 줄 삭제 검사 후 다음 피스를 스폰
    void LockPiece()
    {
        List<Transform> children = new List<Transform>();
        foreach (Transform child in activePiece.transform)
        {
            children.Add(child); // 부모를 바꾸기 전에 먼저 목록으로 복사해둠
        }

        foreach (Transform child in children)
        {
            int col = Mathf.RoundToInt(child.position.x);
            int row = Mathf.RoundToInt(child.position.y);

            child.parent = null; // 피스에서 분리 (월드 위치는 그대로 유지됨)
            boardCells[col, row] = child.gameObject;
        }

        Destroy(activePiece); // 빈 껍데기가 된 피스 오브젝트 제거
        ClearFullLines();

        SpawnShape(nextShape); // 미리 보여주고 있던 모양을 실제로 스폰
        nextShape = GetRandomShape();
        ShowPreview();
    }

    // 해당 줄의 10칸이 전부 채워져 있는지 검사
    bool IsRowFull(int row)
    {
        for (int col = 0; col < 10; col++)
        {
            if (boardCells[col, row] == null) return false;
        }
        return true;
    }

    // 해당 줄의 블록들을 전부 파괴하고 보드 배열에서 비움
    void ClearRow(int row)
    {
        for (int col = 0; col < 10; col++)
        {
            Destroy(boardCells[col, row]);
            boardCells[col, row] = null;
        }
    }

    // fromRow 위쪽에 있던 모든 줄을 한 칸씩 아래로 내림 (지워진 줄을 메우기 위해)
    void ShiftRowsDown(int fromRow)
    {
        for (int row = fromRow; row < 19; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                boardCells[col, row] = boardCells[col, row + 1];
                if (boardCells[col, row] != null)
                {
                    boardCells[col, row].transform.position += new Vector3(0, -1, 0);
                }
            }
        }

        for (int col = 0; col < 10; col++)
        {
            boardCells[col, 19] = null; // 맨 위 줄은 다 내려왔으니 빈 칸 처리
        }
    }

    // 꽉 찬 줄들을 전부 찾아서 삭제하고, 지운 줄 수만큼 점수를 더함
    void ClearFullLines()
    {
        int row = 0;
        int linesCleared = 0;

        while (row < 20)
        {
            if (IsRowFull(row))
            {
                ClearRow(row);
                ShiftRowsDown(row);
                linesCleared++;
                // row를 증가시키지 않음: 위에서 내려온 줄을 같은 자리에서 다시 검사
            }
            else
            {
                row++;
            }
        }

        if (linesCleared > 0)
        {
            AddScore(linesCleared);
        }
    }

    // 한 번에 지운 줄 수(linesCleared)에 따라 점수를 더하고 점수 UI 갱신
    void AddScore(int linesCleared)
    {
        int[] pointsTable = { 0, 100, 300, 500, 800 }; // 인덱스 = 한 번에 지운 줄 수
        int index = Mathf.Min(linesCleared, 4); // 혹시 몰라 배열 범위를 벗어나지 않게 방어
        score += pointsTable[index];
        scoreText.text = $"점수: {score}";
        Debug.Log($"{linesCleared}줄 삭제! 점수: {score}");
    }

    // 주어진 모양(shape)으로 새 피스를 스폰. 스폰 자리가 막혀있으면 게임오버 처리
    void SpawnShape(ShapeData shape)
    {
        activePiece = new GameObject("ActivePiece"); // 코드로 직접 빈 GameObject 생성
        activePiece.transform.position = GridToWorldPosition(4, 18); // 보드 위쪽 중앙 근처

        foreach (Vector2Int cell in shape.cells)
        {
            GameObject block = Instantiate(squarePrefab, activePiece.transform); // 부모를 지정해서 생성
            block.transform.localPosition = new Vector3(cell.x, cell.y, 0); // 부모 기준 상대 위치
            block.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            block.GetComponent<SpriteRenderer>().color = shape.color;
        }

        foreach (Transform child in activePiece.transform)
        {
            int col = Mathf.RoundToInt(child.position.x);
            int row = Mathf.RoundToInt(child.position.y);

            if (boardCells[col, row] != null)
            {
                GameOver(); // 스폰 자리에 이미 블록이 있음 = 꼭대기까지 쌓인 것
                return;
            }
        }
    }

    // 게임 상태를 멈추고 게임오버 관련 UI를 표시
    void GameOver()
    {
        isGameOver = true;
        gameOverText.text = "게임 오버";
        finalScoreText.text = $"최종 점수: {score}";
        restartHintText.text = "R을 눌러 재시작";
        Debug.Log($"게임 오버! 최종 점수: {score}");
    }

    // 보드/점수/시간을 전부 초기화하고 게임을 처음부터 다시 시작
    void ResetGame()
    {
        for (int col = 0; col < 10; col++)
        {
            for (int row = 0; row < 20; row++)
            {
                if (boardCells[col, row] != null)
                {
                    Destroy(boardCells[col, row]);
                    boardCells[col, row] = null;
                }
            }
        }

        if (activePiece != null) Destroy(activePiece);
        if (previewPiece != null) Destroy(previewPiece);

        score = 0;
        elapsedTime = 0f;
        fallTimer = 0f;
        isGameOver = false;

        scoreText.text = "점수: 0";
        timeText.text = "시간: 00:00";
        gameOverText.text = "";
        finalScoreText.text = "";
        restartHintText.text = "";

        nextShape = GetRandomShape();
        SpawnShape(nextShape);
        nextShape = GetRandomShape();
        ShowPreview();
    }

    // 테트로미노 7종의 모양(좌표)과 색을 정의해서 allShapes에 저장
    void InitShapes()
    {
        Vector2Int[] iShape = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) };
        Vector2Int[] oShape = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        Vector2Int[] tShape = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(1, 1) };
        Vector2Int[] sShape = { new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        Vector2Int[] zShape = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(2, 1) };
        Vector2Int[] jShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) };
        Vector2Int[] lShape = { new Vector2Int(2, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) };

        allShapes = new ShapeData[]
        {
            new ShapeData(iShape, Color.cyan),
            new ShapeData(oShape, Color.yellow),
            new ShapeData(tShape, new Color(0.6f, 0.1f, 0.8f)), // 보라
            new ShapeData(sShape, Color.green),
            new ShapeData(zShape, Color.red),
            new ShapeData(jShape, Color.blue),
            new ShapeData(lShape, new Color(1f, 0.55f, 0f)), // 주황
        };
    }

    // 7종 모양 중 하나를 무작위로 골라서 반환
    ShapeData GetRandomShape()
    {
        int randomIndex = Random.Range(0, allShapes.Length);
        return allShapes[randomIndex];
    }

    // 보드 오른쪽에 다음 블록(nextShape) 미리보기를 다시 그림
    void ShowPreview()
    {
        if (previewPiece != null)
        {
            Destroy(previewPiece);
        }

        previewPiece = new GameObject("PreviewPiece");
        previewPiece.transform.position = new Vector3(11, 15, 0); // 보드 오른쪽 옆 미리보기 자리

        foreach (Vector2Int cell in nextShape.cells)
        {
            GameObject block = Instantiate(squarePrefab, previewPiece.transform);
            block.transform.localPosition = new Vector3(cell.x, cell.y, 0);
            block.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            block.GetComponent<SpriteRenderer>().color = nextShape.color;
        }
    }

    // 게임 시작 시 한 번 실행되는 초기 세팅
    void Start()
    {
        Camera.main.backgroundColor = new Color(0.08f, 0.08f, 0.12f); // 어두운 배경으로 블록들이 잘 보이게

        InitShapes();
        CreateWalls();
        CreateUI();

        nextShape = GetRandomShape();
        SpawnShape(nextShape);

        nextShape = GetRandomShape();
        ShowPreview();
    }

    // 매 프레임 실행: 입력 처리(이동/회전/소프트드롭) + 자동 낙하 + 게임오버 시 재시작 감지
    void Update()
    {
        if (isGameOver)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                ResetGame();
            }
            return;
        }

        elapsedTime += Time.deltaTime;
        int minutes = Mathf.FloorToInt(elapsedTime / 60f);
        int seconds = Mathf.FloorToInt(elapsedTime % 60f);
        timeText.text = $"시간: {minutes:00}:{seconds:00}";

        bool leftPressed = Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame;
        bool rightPressed = Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame;
        bool rotateCWPressed = Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame;
        bool rotateCCWPressed = Keyboard.current.qKey.wasPressedThisFrame;
        bool fastFallHeld = Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed;

        if (leftPressed)
        {
            Vector3 offset = new Vector3(-1, 0, 0);
            if (CanMove(offset))
            {
                activePiece.transform.position += offset;
            }
        }
        if (rightPressed)
        {
            Vector3 offset = new Vector3(1, 0, 0);
            if (CanMove(offset))
            {
                activePiece.transform.position += offset;
            }
        }
        if (rotateCWPressed)
        {
            Rotate(true);
        }
        if (rotateCCWPressed)
        {
            Rotate(false);
        }

        float baseFallInterval = Mathf.Max(minFallInterval, fallInterval - elapsedTime * speedUpRate);
        float currentFallInterval = fastFallHeld ? fastFallInterval : baseFallInterval;

        fallTimer += Time.deltaTime;
        if (fallTimer >= currentFallInterval)
        {
            fallTimer = 0f;
            Vector3 fallOffset = new Vector3(0, -1, 0);
            if (CanMove(fallOffset))
            {
                activePiece.transform.position += fallOffset;
            }
            else
            {
                LockPiece();
            }
        }
    }
}

// 테트로미노 하나의 모양(상대 좌표들)과 색을 함께 담는 데이터 클래스
class ShapeData
{
    public Vector2Int[] cells;
    public Color color;

    public ShapeData(Vector2Int[] cells, Color color)
    {
        this.cells = cells;
        this.color = color;
    }
}
