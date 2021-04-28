﻿/**********************************************************************
	Copyright 2021 Misty Robotics
	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at
		http://www.apache.org/licenses/LICENSE-2.0
	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
	**WARRANTY DISCLAIMER.**
	* General. TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, MISTY
	ROBOTICS PROVIDES THIS SAMPLE SOFTWARE "AS-IS" AND DISCLAIMS ALL
	WARRANTIES AND CONDITIONS, WHETHER EXPRESS, IMPLIED, OR STATUTORY,
	INCLUDING THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
	PURPOSE, TITLE, QUIET ENJOYMENT, ACCURACY, AND NON-INFRINGEMENT OF
	THIRD-PARTY RIGHTS. MISTY ROBOTICS DOES NOT GUARANTEE ANY SPECIFIC
	RESULTS FROM THE USE OF THIS SAMPLE SOFTWARE. MISTY ROBOTICS MAKES NO
	WARRANTY THAT THIS SAMPLE SOFTWARE WILL BE UNINTERRUPTED, FREE OF VIRUSES
	OR OTHER HARMFUL CODE, TIMELY, SECURE, OR ERROR-FREE.
	* Use at Your Own Risk. YOU USE THIS SAMPLE SOFTWARE AND THE PRODUCT AT
	YOUR OWN DISCRETION AND RISK. YOU WILL BE SOLELY RESPONSIBLE FOR (AND MISTY
	ROBOTICS DISCLAIMS) ANY AND ALL LOSS, LIABILITY, OR DAMAGES, INCLUDING TO
	ANY HOME, PERSONAL ITEMS, PRODUCT, OTHER PERIPHERALS CONNECTED TO THE PRODUCT,
	COMPUTER, AND MOBILE DEVICE, RESULTING FROM YOUR USE OF THIS SAMPLE SOFTWARE
	OR PRODUCT.
	Please refer to the Misty Robotics End User License Agreement for further
	information and full details:
		https://www.mistyrobotics.com/legal/end-user-license-agreement/
**********************************************************************/

using System;
using System.ComponentModel.DataAnnotations;
using ConversationBuilder.DataModels;

namespace ConversationBuilder.ViewModels
{
	/// <summary>
	/// Holds override RGB Color Values for LED
	/// </summary>
	public class LEDTransitionViewModel
	{
		[Required]
		public string Name { get; set; }

		public string Id { get; set; }

		[Display(Name = "Starting Color")]
		public string StartingRGB { get; set; }


		[Display(Name = "Ending Color")]
		public string EndingRGB { get; set; }

		/// <summary>
		/// LED transition type
		/// </summary>
		[Display(Name = "Transition Pattern")]
		public string Pattern { get; set; } = LEDPatterns.None;

		/// <summary>
		/// Time for transition or between actions
		/// </summary>
		[Display(Name = "Pattern Time in Seconds")]
		public double PatternTime { get; set; }		

		public DateTimeOffset Created { get; set; }
		
		public DateTimeOffset Updated { get; set; }
		
		[Display(Name = "Management Access (beta)")]
		public string ManagementAccess { get; set; } = "Shared";
		public string CreatedBy { get; set; }
	}
}
