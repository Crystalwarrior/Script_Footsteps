exec("./colors.cs");

//PROTIP: EVERYTHING NAMED stupid_dirt1.wav WILL HAVE THE PRE-_ CHARACTER STUFFS STRIPPED OUT.

$FOOTSTEPS_INTERVAL = 300;
$FOOTSTEPS_SWIMINTERVAL = 2000;
$FOOTSTEPS_MIN_LANDING = -1.5;
$FOOTSTEPS_WALKING_FACTOR = 0.5;
$FOOTSTEPS_BLOODYFOOTPRINTS = 1;
$FOOTSTEPS_MAXBLOODSTEPS = 30;

$FOOTSTEPS_SOUNDDIR = "sounds_pegg";
$FOOTSTEPS_WATERMATERIAL = "water";
$FOOTSTEPS_SWIMMINGMATERIAL = "swimming";

if ($pref::server::FS::defaultFootstepSound $= "")
	$pref::server::FS::defaultFootstepSound = "generic";

function Player::updateFootsteps(%this, %lastVert)
{
	cancel(%this.updateFootsteps);

	if (%this.getState() $= "Dead" || %this.isCorpse)
	{
		return;
	}

	%velocity = %this.getVelocity();

	%vert = getWord(%velocity, 2);
	%horiz = vectorLen(setWord(%velocity, 2, 0));
	%maxforward = getMax(2, %this.getMaxForwardSpeed());

	if (%lastVert < $FOOTSTEPS_MIN_LANDING && %vert >= 0)
	{
		%this.getDataBlock().onLand(%this);
	}
	if ((%this.inWater && %this.getWaterCoverage() > 0.5) || (%horiz >= %maxforward * $FOOTSTEPS_WALKING_FACTOR && !%this.isCrouched() && (!%this.getDataBlock().canJet || !%this.triggerState[4])))
	{
		if (!isEventPending(%this.playFootsteps))
		{
			%this.playFootsteps(1);
		}
	}
	else if (isEventPending(%this.playFootsteps))
	{
		cancel(%this.playFootsteps);
	}

	%this.updateFootsteps = %this.schedule(32, "updateFootsteps", %vert);
}

function Player::playFootsteps(%this, %foot)
{
	cancel(%this.playFootsteps);

	if (%this.getState() $= "Dead" || %this.isCorpse)
	{
		return;
	}

	%interval = $FOOTSTEPS_INTERVAL * (%this.running ? 0.75 : 1);

	if (%this.inWater && %this.getWaterCoverage() > 0.5)
	{
		%interval = $FOOTSTEPS_SWIMINTERVAL;
	}

	%this.getDataBlock().onFootstep(%this, %foot);
	%this.playFootsteps = %this.schedule(%interval, "playFootsteps", !%foot);
}

function Player::getFootPosition(%this, %foot)
{
	%base = %this.getPosition();
	%side = vectorCross(%this.getUpVector(), %this.getForwardVector());

	if (!%foot)
	{
		%side = vectorScale(%side, -1);
	}

	//return vectorAdd(%base, vectorScale(%side, 0.3));
	return vectorAdd(%base, vectorScale(%side, 0.4));
}

function Player::getFootObject(%this, %foot)
{
	%pos = %this.getFootPosition(%foot);

	return containerRayCast(
		vectorAdd(%pos, "0 0 0.1"),
		vectorSub(%pos, "0 0 1.1"),
		$TypeMasks::FxBrickObjectType | $TypeMasks::terrainObjectType, %this
	);
}

function Armor::onLand(%this, %obj)
{
	for (%i = 0; %i < 2; %i++)
	{
		%ray = %obj.getFootObject(%i);

		if (!%ray)
		{
			continue;
		}

		if (%obj.bloodyFootprints > 0)
		{
			%obj.doBloodyFootprint(%ray, %i, %obj.bloodyFootprints / %obj.bloodyFootprintsLast);
			%obj.bloodyFootprints--;
		}

		%color = -1;
		%material = $pref::server::FS::defaultFootstepSound;
		if (%ray.getType() & $TypeMasks::FxBrickObjectType)
		{
			%color = %ray.getColorID();
			%prefix = "_fsm_";
			%strpos = strpos(%ray.getName(), %prefix);
			if(%strpos >= 0) //We have detected "fsm_" in the brick's name! That means someone wants us to use the brick as a footstep material.
			{
				%strpos += strlen(%prefix); //search after "fsm_", even if someone decides to name their brick "foobarfsm_"
				%material = getSubStr(%ray.getName(), %strpos, strlen(%ray.getName())); //Find the material name from after "fsm_" to string length.
				if((%strpos = strpos(%material, "_")) >= 0) //Oh crap, we have found an underscore in our new %material! Let's strip out everything after it!
				{
					%material = getSubStr(%material, 0, %strpos); //Boom.
				}
			}
			else
			{
				%material = getMaterial(%color);
			}
		}
		if (%ray.material !$= "")
		{
			%material = %ray.material;
		}

		if (%obj.inWater)
		{
			%material = $FOOTSTEPS_WATERMATERIAL;
		}

		%sound = nameToID("footstepSound_" @ %material @ getRandom(1, $FS::SoundCount[%material]));

		if (isObject(%sound))
		{
			serverPlay3D(%sound, getWords(%ray, 1, 3));
		}
	}
}

function Armor::onFootstep(%this, %obj, %foot)
{
	%ray = %obj.getFootObject(%foot);

	if (!%ray || %obj.isCrouched())
	{
		if (%obj.inWater && %obj.getWaterCoverage() > 0.5)
		{
			%material = $FOOTSTEPS_SWIMMINGMATERIAL;
			%sound = "footstepSound_" @ %material @ getRandom(1, $FS::SoundCount[%material]);
			%sound = nameToID(%sound);

			if (isObject(%sound))
			{
				serverPlay3D(%sound, %obj.getHackPosition());
			}
		}
		return 0;
	}

	if($FOOTSTEPS_BLOODYFOOTPRINTS)
	{
		initContainerRadiusSearch(getWords(%ray, 1, 3), 0.4, $TypeMasks::ShapeBaseObjectType);
		while (%col = containerSearchNext())
		{
			if (%col.isBlood && %col.freshness >= 1)// && %col.sourceClient != %obj.client)
			{
				%obj.setBloodyFootprints(getMin(%obj.bloodyFootprints + %col.freshness * 3, $FOOTSTEPS_MAXBLOODSTEPS), %col.sourceClient); //Default freshness for splatter blood is 3, so 15 footsteps for fresh step.
				%col.freshness--; //Decrease blood freshness
				createBloodExplosion(getWords(%ray, 1, 3), %obj.getVelocity(), %col.getScale());
				serverPlay3d(bloodSpillSound, getWords(%ray, 1, 3));
				break;
			}
		}
		//if (!%obj.isCloaked && 0)
		if (%obj.bloodyFootprints > 0)
		{
			%obj.doBloodyFootprint(%ray, %foot, %obj.bloodyFootprints / %obj.bloodyFootprintsLast);
			%obj.bloodyFootprints--;
		}
	}

	%color = -1;
	%material = $pref::server::FS::defaultFootstepSound;
	if (%ray.getType() & $TypeMasks::FxBrickObjectType)
	{
		%color = %ray.getColorID();
		%prefix = "_fsm_";
		%strpos = strpos(%ray.getName(), %prefix);
		if(%strpos >= 0) //We have detected "fsm_" in the brick's name! That means someone wants us to use the brick as a footstep material.
		{
			%strpos += strlen(%prefix); //search after "fsm_", even if someone decides to name their brick "foobarfsm_"
			%material = getSubStr(%ray.getName(), %strpos, strlen(%ray.getName())); //Find the material name from after "fsm_" to string length.
			if((%strpos = strpos(%material, "_")) >= 0) //Oh crap, we have found an underscore in our new %material! Let's strip out everything after it!
			{
				%material = getSubStr(%material, 0, %strpos); //Boom.
			}
		}
		else
		{
			%material = getMaterial(%color);
		}
	}

	//%p = new Projectile()
	//{
	//  datablock = PongProjectile;
	//  initialPosition = %obj.getFootPosition(%foot);
	//  initialVelocity = "0 0 0";
	//};

	//%p.setScale("0.5 0.01 0.5");

	if (%ray.material !$= "")
	{
		%material = %ray.material;
	}

	if (%obj.inWater)
	{
		%material = $FOOTSTEPS_WATERMATERIAL;
	}

	%sound = "footstepSound_" @ %material @ getRandom(1, $FS::SoundCount[%material]);
	%sound = nameToID(%sound);

	if (isObject(%sound))
	{
		serverPlay3D(%sound, getWords(%ray, 1, 3));
	}
	return 1; //For packages
}

package FootstepsPackage
{
	function Armor::onEnterLiquid(%data, %obj, %coverage, %type)
	{
		Parent::onEnterLiquid(%data, %obj, %coverage, %type);
		%obj.inWater = true;
	}

	function Armor::onLeaveLiquid(%data, %obj, %coverage, %type)
	{
		Parent::onLeaveLiquid(%data, %obj, %coverage, %type);
		%obj.inWater = false;
	}

	function Armor::onNewDataBlock(%this, %obj)
	{
		Parent::onNewDataBlock(%this, %obj);
		if (%obj.isCorpse) return;
		if (%this.rideable && !%this.forceFootsteps) return;
		if (%this.disableFootsteps) return;
		if (!isEventPending(%obj.updateFootsteps))
		{
			%obj.updateFootsteps = %obj.schedule(0, "updateFootsteps");
		}
	}

	function Armor::onTrigger(%this, %obj, %slot, %state)
	{
		Parent::onTrigger(%this, %obj, %slot, %state);
		%obj.triggerState[%slot] = %state ? 1 : 0;
	}
};

activatePackage("FootstepsPackage");

function getNumberStart( %str )
{
	%best = -1;

	for ( %i = 0 ; %i < 10 ; %i++ )
	{
		%pos = strPos( %str, %i );

		if ( %pos < 0 )
		{
			continue;
		}

		if ( %best == -1 || %pos < %best )
		{
			%best = %pos;
		}
	}

	return %best;
}

function loadFootstepSounds()
{
	%pattern = "./"@ $FOOTSTEPS_SOUNDDIR @"/*.wav";
	%list = "generic 0";

	deleteVariables( "$FS::Sound*" );
	$FS::SoundNum = 0;

	for ( %file = findFirstFile( %pattern ); %file !$= ""; %file = findNextFile( %pattern ) )
	{
		%base = fileBase( %file );
		if((%char = strchr(%base, "_")) !$= "")
			%base = getSubStr(%char, 1, strlen(%char));
		%name = "footstepSound_" @ %base;

		echo(%name);

		if ( !isObject( %name ) )
		{
			datablock audioProfile( genericFootstepSound )
			{
				description = "audioClosest3D";
				fileName = %file;
				preload = true;
			};

			if ( !isObject( %obj = nameToID( "genericFootstepSound" ) ) )
			{
				continue;
			}

			%obj.setName( %name );
		}

		if ( ( %pos = getNumberStart( %base ) ) > 0 )
		{
			%pre = getSubStr( %base, 0, %pos );
			%post = getSubStr( %base, %pos, strLen( %base ) );

			if ( $FS::SoundCount[ %pre ] < 1 || !strLen( $FS::SoundCount[ %pre ] ) )
			{
				%list = %list SPC %pre SPC $FS::SoundNum;
			}

			if ( $FS::SoundCount[ %pre ] < %post )
			{
				$FS::SoundCount[ %pre ] = %post;
			}

			$FS::SoundName[ $FS::SoundNum ] = %pre;
			$FS::SoundIndex[ %pre ] = $FS::SoundNum;
			$FS::SoundNum++;
		}
	}

	registerOutputEvent( "fxDTSBrick", "setMaterial", "list" SPC %list );
}

function fxDTSBrick::setMaterial( %this, %idx )
{	
	%this.material = $FS::SoundName[ %idx ];
	echo(%idx);
}

schedule(0, 0, loadFootstepSounds);

//ServerCmdStuff below
//vvvvvvvvvvvvvvvvvvv

function serverCmdClearAllMaterials(%this)
{
	if(!%this.isAdmin)
	{
		messageClient(%this,'',"You must be an admin to use ths command.");
		return;
	}
	// $pref::server::FS::materialColor["material"] = "";
	for (%i=0; %i <= $pref::server::FS::materialNum ; %i++)
	{
		$pref::server::FS::materialIndex[$pref::server::FS::materialName[%i]] = "";
		$pref::server::FS::materialName[%i] = "";
		$pref::server::FS::materialColors[%i] = "";
	}
	$pref::server::FS::materialNum = -1;
	messageClient(%this,'',"\c2You have cleared all materials!");
}

function serverCmdClearMaterial(%this, %material, %all)
{
	if(!%this.isAdmin)
	{
		messageClient(%this,'',"You must be an admin to use ths command.");
		return;
	}

	%color = getColorIDTable(%this.currentColor);
	if ($FS::SoundIndex[%material] $= "")
	{
		messageClient(%this,'',"Invalid material\c2: " @ %material);
		return;
	}

	%index = $pref::server::FS::materialIndex[%material];
	%name = $pref::server::FS::materialName[%index];
	%colors = $pref::server::FS::materialColors[%index];
	if (%name $= %material)
	{
		if (%all == 1)
		{
			$pref::server::FS::materialColors[%index] = "";
			messageClient(%this,'',"\c2Succesfully cleared all associated colors for material \c3"@ %material @"\c2.");
		}
		else
		{
			for (%i = 0; %i < getFieldCount(%colors); %i++)
			{
				if (getField(%colors, %i) $= %color)
				{
					messageClient(%this,'',"\c2Succesfully cleared selected color <color:" @ rgbToHex(vectorScale(%color, 255)) @ ">" @ %color @"\c2 for material \c3"@ %material @"\c2.");
					$pref::server::FS::materialColors[%index] = removeField(%colors, %i);
					if(getFieldCount($pref::server::FS::materialColors[%index]) <= 0)
					{
						$pref::server::FS::materialIndex[$pref::server::FS::materialName[%index]] = "";
						$pref::server::FS::materialName[%index] = "";
						$pref::server::FS::materialColors[%index] = "";
					}
					return;
				}
			}
			messageClient(%this,'',"Material\c2: " @ %material @ "\c0 doesn't have that color set!");
		}
		return;
	}
	messageClient(%this,'',"Material\c2: " @ %material @ "\c0 doesn't have any colors set!");
}

function serverCmdSetColorToMaterial(%this, %material)
{
	if(!%this.isAdmin)
	{
		messageClient(%this,'',"You must be an admin to use ths command.");
		return;
	}
	%color = getColorIDTable(%this.currentColor);
	if ($FS::SoundIndex[%material] $= "")
	{
		messageClient(%this,'',"Invalid material\c2: " @ %material);
		return;
	}

	for (%i=0; %i <= $pref::server::FS::materialNum; %i++)
	{
		%name = $pref::server::FS::materialName[%i];
		%colors = $pref::server::FS::materialColors[%i];
		for (%a = 0; %a < getFieldCount(%colors); %a++)
		{
			if (getField(%colors, %a) $= %color)
			{
				messageClient(%this,'',"Error\c2: \c3" @ $pref::server::FS::materialName[%i] @ "\c2 is already set to selected color <color:" @ rgbToHex(vectorScale(%color, 255)) @ ">" @ %color @"\c2.");
				return;
			}
		}
		if (%name $= %material) //material already exists in prefs
		{
			$pref::server::FS::materialColors[%i] = %colors TAB %color;
			messageClient(%this,'',"\c2Added selected color <color:" @ rgbToHex(vectorScale(%color, 255)) @ ">" @ %color @"\c2 to the material \c3" @ %material @ "\c2.");
			return;
		}
	}
	$pref::server::FS::materialName[$pref::server::FS::materialNum++] = %material;
	$pref::server::FS::materialColors[$pref::server::FS::materialNum] = %color;
	$pref::server::FS::materialIndex[%material] = $pref::server::FS::materialNum;
	messageClient(%this,'',"\c2Added selected color <color:" @ rgbToHex(vectorScale(%color, 255)) @ ">" @ %color @"\c2 to the material \c3" @ %material @ "\c2.");
}

function serverCmdListMaterialColors(%this, %material)
{
	if(!%this.isAdmin)
	{
		messageClient(%this,'',"You must be an admin to use ths command.");
		return;
	}
	if(%material $= "")
	{
		%material = "ALL";
	}

	if ($FS::SoundIndex[%material] $= "" && %material !$= "ALL")
	{
		messageClient(%this,'',"Invalid material\c2: " @ %material);
		return;
	}

	for (%i=0; %i <= $pref::server::FS::materialNum; %i++)
	{
		%name = $pref::server::FS::materialName[%i];
		%colors = $pref::server::FS::materialColors[%i];

		if (%name !$= "" && (%name $= %material || %material $= "ALL"))
		{
			%matcnt += getFieldCount(%colors);
			%text[%count++] = "\c2Material \c3" @ %name @ "\c2 colors:";
			%text[%count++] = "  \c3::";
			%c = 0;
			for (%a = 0; %a < getFieldCount(%colors); %a++)
			{
				if(%c >= 4)
				{
					%c = 0;
					%text[%count++] = "  \c3::";
				}
				%c++;
				%colorfield = getField(%colors, %a);
				%fl = 3;
				%colorfinal = mFloatLength(getWord(%colorfield, 0), %fl) SPC mFloatLength(getWord(%colorfield, 1), %fl) SPC mFloatLength(getWord(%colorfield, 2), %fl);
				%text[%count] = %text[%count] @ "\c2 / " @ "<color:" @ rgbToHex(vectorScale(%colorfield, 255)) @ ">" @ %colorfinal;
			}
		}
	}
	for (%i=1; %i<=%count; %i++)
	{		
		messageClient(%this, '', %text[%i]);
	}
	messageClient(%this, '', "\c2Listed \c3" @ (%matcnt || 0) @ "\c2 materials!");
}

function serverCmdFootstepsHelp(%this)
{
	messageClient(%this,'',"\c3/ClearAllMaterials \c2- clears all associated material colors");
	messageClient(%this,'',"\c3/ClearMaterial [Material] ( [bool] ) \c2- Clears selected paint-can color for the [Material]. [bool] must be 1 to clear ALL colors for [Material], otherwise leave blank.");
	messageClient(%this,'',"\c3/SetColorToMaterial [Material] \c2- Associates paint-can color with [Material].");
	messageClient(%this,'',"\c3/ListMaterialColors [Material] \c2- Lists all colors associated with [Material]. Set [Material] to 'ALL' to list all material colors.");
}

function getMaterial(%color)
{
	if (getWordCount(%color) <= 1)
		%color = getColorIDTable(%color);

	for (%i=0; %i <= $pref::server::FS::materialNum; %i++)
	{
		%name = $pref::server::FS::materialName[%i];
		%colors = $pref::server::FS::materialColors[%i];
		for (%a = 0; %a < getFieldCount(%colors); %a++)
		{
			if (getField(%colors, %a) $= %color)
			{
				return %name;
			}
		}
	}

	return $pref::server::FS::defaultFootstepSound;
}