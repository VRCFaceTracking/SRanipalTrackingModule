﻿//========= Copyright 2019, HTC Corporation. All rights reserved. ===========
namespace ViveSR
{
	/// <summary>
	/// error code of ViveSR
	/// </summary>
	public enum Error : int
	{
		RUNTIME_NOT_FOUND 			= -3,
		NOT_INITIAL 				= -2,
		FAILED 						= -1,
		WORK 						= 0,
		INVALID_INPUT 				= 1,
		FILE_NOT_FOUND 				= 2,
		DATA_NOT_FOUND 				= 13,
		UNDEFINED 					= 319,
		INITIAL_FAILED 				= 1001,
		NOT_IMPLEMENTED 			= 1003,
		NULL_POINTER 				= 1004,
		OVER_MAX_LENGTH 			= 1005,
		FILE_INVALID 				= 1006,
		UNINSTALL_STEAM 			= 1007,
		MEMCPY_FAIL 				= 1008,
		NOT_MATCH 					= 1009,
		NODE_NOT_EXIST 				= 1010,
		UNKONW_MODULE 				= 1011,
		MODULE_FULL 				= 1012,
		UNKNOW_TYPE 				= 1013,
		INVALID_MODULE 				= 1014,
		INVALID_TYPE 				= 1015,
		MEMORY_NOT_ENOUGH 			= 1016,
		BUSY 						= 1017,
		NOT_SUPPORTED				= 1018,
		INVALID_VALUE 				= 1019,
		COMING_SOON 				= 1020,
		INVALID_CHANGE 				= 1021,
		TIMEOUT 					= 1022,
		DEVICE_NOT_FOUND 			= 1023,
		INVALID_DEVICE 				= 1024,
		NOT_AUTHORIZED 				= 1025,
		ALREADY 					= 1026,
		INTERNAL 					= 1027,
		CONNECTION_FAILED 			= 1028,
		ALLOCATION_FAILED 			= 1029,
		OPERATION_FAILED			= 1030,
		NOT_AVAILABLE 				= 1031,
		CALLBACK_IN_PROGRESS 		= 1032,
		SERVICE_NOT_FOUND 			= 1033,
		DISABLED_BY_USER 			= 1034,
		EULA_NOT_ACCEPT		 		= 1035,
		RUNTIME_NO_RESPONSE 		= 1036,
		OPENCL_NOT_SUPPORT 			= 1037,
		NOT_SUPPORT_EYE_TRACKING 	= 1038,
		FOXIP_SO 					= 1051   // Weird wireless issue
	};
}
