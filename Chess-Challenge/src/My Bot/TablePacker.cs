using System;
using System.Collections.Generic;

namespace Chess_Challenge.My_Bot;

public class TablePacker
{
    sbyte[,] pawnOpeningScores = 
    {
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 25, -10, -30,  -30},
        { 25, 0, -10, -10},
        { 0, 25,  0,  0},
        { 0, 25, 50, 50},
        { 0,  0,  0,  0}
    };
    sbyte[,] pawnScores = 
    {
        { 0,  0,  0,  0},
        { 0,  0,  -10,  -10},
        { 0,  0,  -10,  -10},
        { 25,  0, -10, -10},
        { 25,  0,  0,  0},
        { 0, 20, 20, 20},
        { 0, 20, 40, 40},
        { 0,  0,  0,  0}
    };
    sbyte[,] knightScores = 
    {
        { -50, -50, -50, -50},
        { -25, -25, -25, -50},
        { 0,  0, -25, -50},
        { 10,  0, -25, -50},
        { 10,  0, -25, -50},
        { 0,  0, -25, -50},
        { -25, -25, -25, -50},
        { -50, -50, -50, -50}
    };
    sbyte[,] bishopScores = 
    {
        { -10, -10, -10, -10},
        { 0,  0,  0,  0},
        { 5,  5,  0, -10},
        { 10,  5,  0, -10},
        { 10,  5,  0, -10},
        { 5,  5,  0, -10},
        { 0,  0,  0, -10},
        { -10, -10, -10, -10}
    };
    sbyte[,] rookScores = 
    {
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0}
    };
    sbyte[,] queenScores = 
    {
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0}
    };
    sbyte[,] kingScores = 
    {
        { -80, -70, -70, -70},
        { -70, -60, -60, -60},
        { -60, -50, -50, -50},
        { -50, -20, -20, -20},
        { -20, -10, -10, -10},
        { -10,  0,  0,  0},
        { 0,  0,  25,  25},
        { 0,  25,  50,  50}
    };
    sbyte[,] kingEndgameScores = 
    {
        { 0,  0,  0,  0},
        { 0,  0,  0,  0},
        { 5,  5,  0,  0},
        { 10,  5,  0,  0},
        { 10,  5,  0,  0},
        { 5,  5,  0,  0},
        { 0,  0,  0,  0},
        { 0,  0,  0,  0}
    };
    
    //Use to print the packed array to the console, then clean up and paste directly into your code.
    public void PackScoreData()
    {
        //Add boards from "index" 0 upwards. Here, the pawn board is "index" 0.
        //That means it will occupy the least significant byte in the packed data.
        List<sbyte[,]> allScores = new();
        allScores.Add(pawnScores);
        allScores.Add(knightScores);
        allScores.Add(bishopScores);
        allScores.Add(rookScores);
        allScores.Add(queenScores);
        allScores.Add(kingScores);
        allScores.Add(pawnOpeningScores);
        allScores.Add(kingEndgameScores);

        ulong[,] packedData = new ulong[8,4];
        for(sbyte rank = 0; rank < 8; rank++)
        {
            for(sbyte file = 0; file < 4; file++)
            {
                for(sbyte set = 0; set < 8; set++)
                {
                    //This is slightly inefficient but you only need to run this code once so it's fine
                    sbyte[,] thisSet = allScores[set];
                    //You could argue this should be |= but either operator works since no two digits overlap.
                    packedData[rank,file] += ((ulong)thisSet[rank,file]) << (8 * set);
                }
            }
            Console.WriteLine("{{0x{0,16:X}, 0x{1,16:X}, 0x{2,16:X}, 0x{3,16:X}}},", packedData[rank,0], packedData[rank,1], packedData[rank,2], packedData[rank,3]);
        }
    }
}