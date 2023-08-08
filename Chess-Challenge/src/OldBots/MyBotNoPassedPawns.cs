#define LOGGING

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ChessChallenge.API;

namespace Chess_Challenge.My_Bot;

public class MyBotNoPassedPawns : IChessBot {
    
    struct TreeNode
        {
            public float Visits;
            public float Value;
            public TreeNode[] Children;
            public Move[] Moves;
        }

        Board board;

        TreeNode root;

        public Move Think(Board startBoard, Timer timer)
        {
            board = startBoard;

            Boolean foundNewRoot = false;
            if (root.Children != null && root.Children.Length > 0)
            {

                for (int i = 0; i < root.Moves.Length; i++)
                    if (root.Moves[i] == board.GameMoveHistory.Last())
                    {
                        root = root.Children[i];
                        foundNewRoot = true;
                        break;
                    }
            }

            if (!foundNewRoot) root = new TreeNode();

            while (timer.MillisecondsElapsedThisTurn < timer.MillisecondsRemaining / 20 && root.Visits < 900000)
                iteration(ref root);

            
            Move bestMove;
            if (root.Moves == null || root.Moves.Length == 0) bestMove = board.GetLegalMoves()[0];
            else
            {
                float bestAvg = -1;
                bestMove = root.Moves[0];
            
                for (int i = 0; i < root.Moves.Length; i++)
                {
                    TreeNode child = root.Children[i];
                    float avg = -child.Value / child.Visits;

                    if (avg > bestAvg)
                    {
                        bestAvg = avg;
                        bestMove = root.Moves[i];
                    }
                }
            }
            
            return bestMove;
        }

        float iteration(ref TreeNode node)
        {
            if (node.Visits == 0)
            {
                node.Visits = 1;
                node.Value = evaluation();
                return node.Value;
            }

            if (node.Visits == 1)
            {
                node.Moves = board.GetLegalMoves();
                node.Children = new TreeNode[node.Moves.Length];
            }

            if (node.Moves.Length == 0)
                return node.Value / node.Visits;

            float part = 1.41f * MathF.Log(node.Visits);
            float bestUCT = -1;

            int bestChildIdx = 0;

            for (int i = 0; i < node.Moves.Length; i++)
            {
                TreeNode child = node.Children[i];

                float uct;
                if (child.Visits == 0)
                    uct = 10f;
                else uct = (-child.Value / child.Visits) + MathF.Sqrt(part / child.Visits);

                //uct += GetMovePriority(node.Moves[i]) / node.Visits;

                if (uct >= bestUCT)
                {
                    bestUCT = uct;
                    bestChildIdx = i;
                }
            }
            
            Move exploreMove = node.Moves[bestChildIdx];
            board.MakeMove(exploreMove);
            float eval = -iteration(ref node.Children[bestChildIdx]);
            node.Value += eval;
            node.Visits++;
            board.UndoMove(exploreMove);

            return eval;
        }

    int[] pieceWeights = {100,280,320,500,900};
    ulong bordermagic = 18411139144890810879;
    
    float evaluation()
    {
        if (board.IsRepeatedPosition() || board.IsInsufficientMaterial() || board.FiftyMoveCounter >= 50) return -5f;
        if (board.IsInCheckmate()) return -10f;
        
        float score = 0;

        for (int i = 1; i < 6; i++)
        {
            ulong whitePieces = board.GetPieceBitboard((PieceType)i, true);
            ulong blackPieces = board.GetPieceBitboard((PieceType)i, false);
            score += pieceWeights[i - 1] * (BitOperations.PopCount(whitePieces) - BitOperations.PopCount(blackPieces));
            score -= 15 * (BitOperations.PopCount(whitePieces & bordermagic) - BitOperations.PopCount(blackPieces & bordermagic));
        }

        //score += NumPassedPawn(true) - NumPassedPawn(false);
        
        if (!board.IsWhiteToMove) score = -score;

        return 0.9f * MathF.Tanh(score / 250);
    }
    
    bool IsPassedPawn(Square square, PieceType piece, bool isWhite)
    {
        int numPawnsInRank = 0;
        PieceList pieces = board.GetPieceList(PieceType.Pawn, isWhite);

        if (piece == PieceType.Pawn)
        {
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i].Square.Rank == square.Rank) numPawnsInRank++;

            if (numPawnsInRank == 1) return true;
        }
        return false;
    }

    int NumPassedPawn(bool isWhite)
    {
        int numPassedPawns = 0;
        PieceList pieces = board.GetPieceList(PieceType.Pawn, isWhite);

        for (int i = 0; i < pieces.Count; i++)
        {
            if (IsPassedPawn(pieces.GetPiece(i).Square, PieceType.Pawn, isWhite))
                numPassedPawns++;
        }
        return numPassedPawns;
    }
}