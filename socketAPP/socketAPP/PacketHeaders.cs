﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace socketAPP
{

        public enum WorldCommand : uint
        {
            SMSG_OFFER_ENTER = 0,
            CMSG_OFFER_ENTER_ANSWER = 1,

            SMSG_START_GAME = 2,

            CMSG_OBJ_INFO = 3,
            SMSG_OBJ_INFO = 4,
            CMSG_OBJ_CREATE = 5,
            SMSG_OBJ_CREATE = 6,
            CMSG_PLAYER_LOGIN = 7,
            SMSG_PLAYER_LOGIN = 8,
            SMSG_CREATE_PLAYERS = 9,

            CMSG_CREATE_BULLET = 10,
            SMSG_CREATE_BULLET = 11
    }
}
