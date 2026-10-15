# Third-party assets

Policy: only CC0 / CC-BY content is downloaded. Exceptions that were already in the project before S6 and are kept on purpose: Mixamo character/animations (Adobe Mixamo terms, royalty-free in projects) and the Meta XR SDK (Oculus SDK License).

| Asset | Author / Source | License |
|---|---|---|
| Character "Swat Guy" + animations (SourceAssets/Mixamo) | Adobe Mixamo, https://www.mixamo.com | Mixamo terms: royalty-free use in projects (raw files must not be redistributed standalone) |
| Meta XR SDK 207 | Meta | Oculus SDK License |
| Erlan.fbx + textures (Assets/Art/Characters/Erlan) | Derived from Mixamo "Swat Guy": decimated, retinted to olive/khaki digital camo, plate carrier added (tools/blender/prepare_casualty.py) | Mixamo terms (as above) |
| Mixamo animation FBX copies (Assets/Art/Characters/Erlan/Anims) | Adobe Mixamo | Mixamo terms |
| Gear models, amputation stump, blood/wound textures | Procedural, made for this project (tools/blender/prepare_casualty.py, tools/make_blood_textures.py) | Project-owned |
| Voice placeholders (Assets/Audio/Voice) | Generated with Windows TTS "Microsoft Irina" + tools/voice_post.py; to be replaced by real recordings | Project-owned placeholder |
| Radio commander + training instructor voices (Assets/Resources/Radio) | Generated with Windows TTS "Microsoft Irina" + tools/radio_post.py (radio band-pass/squelch); to be replaced by real recordings | Project-owned placeholder |
| Hand visuals, controller models, HandTracking_uvmap texture | Meta XR Core SDK sample assets (OVRHandPrefab / OVRControllerPrefab) | Oculus SDK License |
| UI font LegacyRuntime.ttf | Unity built-in resource | Unity Editor license |
| Trench textures (Assets/Art/Environment/Textures/Env_*): excavated_soil_wall, brown_mud_02, burned_ground_01, raw_plank_wall, bark_brown_02, hessian_230, green_metal_rust (1k JPG, smoothness packed by tools/textures/prep_env_textures.py) | Poly Haven, https://polyhaven.com/textures (authors listed per asset page, e.g. Rob Tuytel, Dimitrios Savva, Charlotte Baglioni) | CC0 1.0 |
| Trench geometry, sky, net/FX textures, shaders (NightSky, EnvLit, Particle) | Procedural, made for this project (Assets/Editor/SceneBuilders/TrenchBuilder.cs, AtmosphereBuilder.cs) | Project-owned |

<!-- SFX-BEGIN -->
## Sound effects (Freesound.org, via tools/fetch_sounds.ps1)

| File (Assets/Audio/SFX/) | Title | Author | Source | License |
|---|---|---|---|---|
| explosion_near/344686_mortar_grenade_shell_002.mp3 | mortar  grenade shell 002.wav | klankbeeld | https://freesound.org/s/344686/ | CC BY 4.0 |
| explosion_near/529239_mortar.mp3 | Mortar | Mozfoo | https://freesound.org/s/529239/ | CC0 1.0 |
| explosion_near/140727_tinymortar.mp3 | tinymortar.wav | j1987 | https://freesound.org/s/140727/ | CC0 1.0 |
| explosion_near2/609587_grenade_explosion_sfx_medium_siz.mp3 | Grenade Explosion SFX (medium-sized, mea | unfa | https://freesound.org/s/609587/ | CC0 1.0 |
| explosion_near2/108641_nearby_explosion_with_debris.mp3 | Nearby explosion with debris.wav | juskiddink | https://freesound.org/s/108641/ | CC BY 4.0 |
| explosion_near2/523365_bomb_explosion.mp3 | Bomb Explosion | LilMati | https://freesound.org/s/523365/ | CC0 1.0 |
| explosion_near2/563010_explosion.mp3 | Explosion.mp3 | florianreichelt | https://freesound.org/s/563010/ | CC0 1.0 |
| explosion_far/108640_distant_explosion.mp3 | Distant explosion.wav | juskiddink | https://freesound.org/s/108640/ | CC BY 4.0 |
| explosion_far/209769_explosion_distant.mp3 | Explosion Distant  | Johnnyfarmer | https://freesound.org/s/209769/ | CC0 1.0 |
| explosion_far/320788_distant_explosions.mp3 | distant explosions | Kostrava | https://freesound.org/s/320788/ | CC0 1.0 |
| incoming_whistle/241840_incoming_mortar_1.mp3 | Incoming mortar 1 | Zagge28 | https://freesound.org/s/241840/ | CC0 1.0 |
| incoming_whistle/170991_incoming_artillery.mp3 | Incoming Artillery.wav | bendodge | https://freesound.org/s/170991/ | CC BY 4.0 |
| incoming_whistle/325258_whistle_nightmare.mp3 | Whistle nightmare.wav | deleted_user_2104797 | https://freesound.org/s/325258/ | CC0 1.0 |
| artillery_outgoing/161343_howitzer_artillery_gun_shot_soun.mp3 | Howitzer Artillery Gun Shot Sound Effect | qubodup | https://freesound.org/s/161343/ | CC0 1.0 |
| artillery_outgoing/161341_howitzer_artillery_gun_shot_soun.mp3 | Howitzer Artillery Gun Shot Sound Effect | qubodup | https://freesound.org/s/161341/ | CC0 1.0 |
| artillery_outgoing/186945_cannon_boom5.mp3 | cannon_boom5.wav | ReadeOnly | https://freesound.org/s/186945/ | CC0 1.0 |
| artillery_outgoing/565794_distant_cannon_fire_simulated.mp3 | Distant Cannon Fire (simulated) | stomachache | https://freesound.org/s/565794/ | CC0 1.0 |
| debris_dirt/109360_stone_falls_and_breaks_low_pitch.mp3 | stone falls and breaks low pitch.aiff | SoundCollectah | https://freesound.org/s/109360/ | CC0 1.0 |
| debris_dirt/325268_brick_on_soil.mp3 | Brick on soil.wav | deleted_user_2104797 | https://freesound.org/s/325268/ | CC0 1.0 |
| debris_dirt/477393_steps_dirt_falling_1a.mp3 | Steps-Dirt-Falling_1a.OGG | nuFF3 | https://freesound.org/s/477393/ | CC BY 4.0 |
| debris_dirt/438524_g33_18_explosion_with_debris.mp3 | G33-18-Explosion with Debris.wav | craigsmith | https://freesound.org/s/438524/ | CC0 1.0 |
| drone_fpv/677833_tiny_hawk_2_short_flight.mp3 | Tiny Hawk 2 short flight.wav | Sadiquecat | https://freesound.org/s/677833/ | CC0 1.0 |
| drone_fpv/201923_low_dive_bomb_drones.mp3 | Low Dive Bomb Drones | DoctorSample314159 | https://freesound.org/s/201923/ | CC0 1.0 |
| drone_fpv/733937_swoosh_05.mp3 | Swoosh 05 | LimitlessDrones | https://freesound.org/s/733937/ | CC0 1.0 |
| drone_fpv2/383852_mini_quadcopter_flying_loop.mp3 | Mini Quadcopter Flying Loop | simeonradivoev | https://freesound.org/s/383852/ | CC0 1.0 |
| drone_fpv2/250085_phantom_quadcopter_takes_off.mp3 | Phantom Quadcopter takes off | 4wdcowboy | https://freesound.org/s/250085/ | CC BY 4.0 |
| drone_mavic/250086_phantom_quadcopter_hovers.mp3 | Phantom Quadcopter Hovers | 4wdcowboy | https://freesound.org/s/250086/ | CC BY 4.0 |
| drone_mavic/529055_drone_startup_sound_dji_mavic_ai.mp3 | Drone Startup Sound (DJI Mavic Air 2).wa | welcometoelmundo | https://freesound.org/s/529055/ | CC0 1.0 |
| drone_mavic/272595_drone6.mp3 | drone6.wav | phantastonia | https://freesound.org/s/272595/ | CC BY 4.0 |
| drone_shahed/78651_moped_motor.mp3 | Moped Motor.wav | JustinBW | https://freesound.org/s/78651/ | CC BY 4.0 |
| drone_shahed/198611_moped_passing.mp3 | moped passing.wav | extrafonia | https://freesound.org/s/198611/ | CC BY 4.0 |
| drone_shahed/192354_solex30sek.mp3 | Solex30sek.mp3 | driet | https://freesound.org/s/192354/ | CC BY 4.0 |
| gunfire_distant/170478_distant_ww2_gunfire_kent.mp3 | Distant WW2 Gunfire Kent.wav | Cheeseheadburger | https://freesound.org/s/170478/ | CC BY 4.0 |
| gunfire_distant/158978_distant_gunfire_02.mp3 | Distant_gunfire_02.wav | CGEffex | https://freesound.org/s/158978/ | CC BY 4.0 |
| gunfire_distant/158977_distant_gunfire_03.mp3 | Distant_gunfire_03.wav | CGEffex | https://freesound.org/s/158977/ | CC BY 4.0 |
| gunfire_distant/160771_maxim_russian_machinegun_distant.mp3 | Maxim Russian machinegun distant | poissonmort | https://freesound.org/s/160771/ | CC BY 4.0 |
| gunfire_ak/163457_ak47_shot.mp3 | AK47 Shot | LeMudCrab | https://freesound.org/s/163457/ | CC0 1.0 |
| gunfire_ak/520935_ak_47_burst.mp3 | ak 47 - burst.mp3 | ninebilly | https://freesound.org/s/520935/ | CC0 1.0 |
| gunfire_ak/17130_ak47_chamber_round.mp3 | ak47_ chamber_round.wav | NoiseCollector | https://freesound.org/s/17130/ | CC BY 4.0 |
| bullet_whiz/505238_whizzby.mp3 | Whizzby | Daleonfire | https://freesound.org/s/505238/ | CC0 1.0 |
| bullet_whiz/730351_bullet_fly_by.mp3 | Bullet Fly By | GO_s0unds123 | https://freesound.org/s/730351/ | CC0 1.0 |
| bullet_whiz/134024_bullet_passbys.mp3 | Bullet passbys.wav | Audionautics | https://freesound.org/s/134024/ | CC BY 4.0 |
| bullet_impact_dirt/150839_bullet_impact_3.mp3 | Bullet_Impact_3 | toxicwafflezz | https://freesound.org/s/150839/ | CC BY 4.0 |
| bullet_impact_dirt/150838_bullet_impact_2.mp3 | Bullet_Impact_2 | toxicwafflezz | https://freesound.org/s/150838/ | CC BY 4.0 |
| bullet_impact_dirt/150837_bullet_impact_1.mp3 | Bullet_Impact_1 | toxicwafflezz | https://freesound.org/s/150837/ | CC BY 4.0 |
| flare/348767_road_flare_ignite_burns.mp3 | road flare ignite burns.wav | frankelmedico | https://freesound.org/s/348767/ | CC0 1.0 |
| flare/317834_road_flare1.mp3 | Road Flare1 | theshaggyfreak | https://freesound.org/s/317834/ | CC BY 4.0 |
| flare/423109_guns_explosions_album_flare_gun.mp3 | Guns & Explosions Album - Flare gun 5-2. | OGsoundFX | https://freesound.org/s/423109/ | CC BY 4.0 |
| radio/524205_radio_sign_off_squelch.mp3 | Radio Sign Off / Squelch | JovianSounds | https://freesound.org/s/524205/ | CC0 1.0 |
| radio/47646_end_radio_transmission.mp3 | End radio transmission | ReadeOnly | https://freesound.org/s/47646/ | CC0 1.0 |
| radio/337747_radio_interference.mp3 | Radio interference | quetzalcontla | https://freesound.org/s/337747/ | CC BY 4.0 |
| radio/561847_foley_object_small_radio_interfe.mp3 | Foley_Object_Small_Radio_Interferences_M | Nox_Sound | https://freesound.org/s/561847/ | CC0 1.0 |
| tinnitus/365956_ear_ringing_after_explosion.mp3 | Ear Ringing (After explosion) | dbdarby | https://freesound.org/s/365956/ | CC0 1.0 |
| tinnitus/188201_ringing_loop.mp3 | Ringing loop.wav | SpliceSound | https://freesound.org/s/188201/ | CC0 1.0 |
| tinnitus/718562_ear_ringing.mp3 | ear ringing | MrGungus | https://freesound.org/s/718562/ | CC0 1.0 |
| heartbeat/232481_rwheart.mp3 | RWHeart.wav | bluejello13 | https://freesound.org/s/232481/ | CC0 1.0 |
| heartbeat/216221_heartbeat_03.mp3 | Heartbeat_03.wav | RSilveira_88 | https://freesound.org/s/216221/ | CC BY 4.0 |
| heartbeat/793474_heartbeat_recorded_with_contact.mp3 | Heartbeat recorded with Contact Micropho | Schacht | https://freesound.org/s/793474/ | CC BY 4.0 |
| breath_heavy/554307_scared_male_heavy_breathing.mp3 | Scared Male Heavy Breathing | casiba842 | https://freesound.org/s/554307/ | CC0 1.0 |
| breath_heavy/319103_heavy_breathing.mp3 | Heavy_Breathing.wav | AbuNijmeh | https://freesound.org/s/319103/ | CC0 1.0 |
| breath_heavy/317478_breathless_panting_2.mp3 | Breathless Panting_2 | Drkvixn91 | https://freesound.org/s/317478/ | CC0 1.0 |
| breath_heavy/432997_male_snore_1.mp3 | Male Snore 1.wav | mattyharm | https://freesound.org/s/432997/ | CC0 1.0 |
| pain_groan/257706_groaning.mp3 | GROANING.wav | vmgraw | https://freesound.org/s/257706/ | CC0 1.0 |
| pain_groan/464486_male_grunting_in_pain.mp3 | Male Grunting In Pain | elynch0901 | https://freesound.org/s/464486/ | CC0 1.0 |
| pain_groan/413186_male_hurt9.mp3 | male_hurt9.ogg | micahlg | https://freesound.org/s/413186/ | CC0 1.0 |
| pain_groan/344681_pain_part_2_groan_torture_suffer.mp3 | Pain part 2: groan, torture, suffering,  | kentspublicdomain | https://freesound.org/s/344681/ | CC0 1.0 |
| pain_scream/179325_male_scream_7.mp3 | Male Scream 7 | jorickhoofd | https://freesound.org/s/179325/ | CC BY 4.0 |
| pain_scream/381942_man_screaming_i.mp3 | Man Screaming I | iplanella | https://freesound.org/s/381942/ | CC BY 4.0 |
| pain_scream/179317_male_scream_13.mp3 | Male Scream 13 | jorickhoofd | https://freesound.org/s/179317/ | CC BY 4.0 |
| pain_scream/695386_male_death_cry_3.mp3 | Male Death Cry 3.wav | F.M.Audio | https://freesound.org/s/695386/ | CC BY 4.0 |
| gasp_choke/514794_wheezing.mp3 | Wheezing | calmingcrunch | https://freesound.org/s/514794/ | CC0 1.0 |
| gasp_choke/252236_no_air.mp3 | no air.wav | Reitanna | https://freesound.org/s/252236/ | CC0 1.0 |
| gasp_choke/403675_sfx_breaths_gasp.mp3 | sfx breaths gasp.mp3 | DBKEEBLER | https://freesound.org/s/403675/ | CC BY 4.0 |
| gasp_choke/582433_wheezing.mp3 | wheezing.wav | gabrielrendon96 | https://freesound.org/s/582433/ | CC0 1.0 |
| cough/537150_male_cough.mp3 | Male cough | dastudiospr | https://freesound.org/s/537150/ | CC0 1.0 |
| cough/743357_dry_slow_double_cough.mp3 | Dry Slow Double Cough | qubodup | https://freesound.org/s/743357/ | CC0 1.0 |
| cough/743354_double_cough.mp3 | Double Cough | qubodup | https://freesound.org/s/743354/ | CC0 1.0 |
| cough/466874_adult_male_coughing.mp3 | Adult Male Coughing | jmbland94 | https://freesound.org/s/466874/ | CC0 1.0 |
| velcro/536231_velcro_pull_01.mp3 | Velcro Pull 01 | Rudmer_Rotteveel | https://freesound.org/s/536231/ | CC0 1.0 |
| velcro/536229_velcro_pull_03.mp3 | Velcro Pull 03 | Rudmer_Rotteveel | https://freesound.org/s/536229/ | CC0 1.0 |
| velcro/536228_velcro_pull_short.mp3 | Velcro Pull Short | Rudmer_Rotteveel | https://freesound.org/s/536228/ | CC0 1.0 |
| velcro/28549_velcro4.mp3 | velcro4.wav | HerbertBoland | https://freesound.org/s/28549/ | CC BY 4.0 |
| scissors_fabric/490114_cutting_fabric.mp3 | cutting fabric | manuelaurreaf | https://freesound.org/s/490114/ | CC BY 4.0 |
| scissors_fabric/649923_cutting_fabric.mp3 | cutting fabric.wav | SamRam21 | https://freesound.org/s/649923/ | CC BY 4.0 |
| scissors_fabric/627627_clothes_fabric_cut_down_scissors.mp3 | Clothes - fabric - cut down - scissors - | ValentinPetiteau | https://freesound.org/s/627627/ | CC0 1.0 |
| package_tear/188203_opening_cheese_slice_packet.mp3 | Opening cheese slice packet.wav | SpliceSound | https://freesound.org/s/188203/ | CC0 1.0 |
| package_tear/477369_opening_a_candy_wrapper.mp3 | Opening a Candy Wrapper | danlucaz | https://freesound.org/s/477369/ | CC0 1.0 |
| package_tear/391473_candy_bar_wrapper.mp3 | Candy Bar Wrapper | coltures | https://freesound.org/s/391473/ | CC0 1.0 |
| fabric_rustle/19291_foley_cloth_rustle.mp3 | foley cloth rustle.wav | martian | https://freesound.org/s/19291/ | CC0 1.0 |
| fabric_rustle/235243_jacket_rustle_aggressive_2.mp3 | Jacket Rustle_Aggressive 2.wav | jaredpass24 | https://freesound.org/s/235243/ | CC0 1.0 |
| fabric_rustle/475440_cloth_1_rustle_fabric_clothes.mp3 | Cloth_1(rustle, fabric, clothes).WAV | o_ciz | https://freesound.org/s/475440/ | CC0 1.0 |
| body_drag/44406_bodydrag1.mp3 | BodyDrag1.aif | bennychico11 | https://freesound.org/s/44406/ | CC BY 4.0 |
| body_drag/268909_dragging.mp3 | dragging.WAV | caoimhe_s1 | https://freesound.org/s/268909/ | CC0 1.0 |
| body_drag/44407_bodydrag2.mp3 | BodyDrag2.aif | bennychico11 | https://freesound.org/s/44407/ | CC BY 4.0 |
| footsteps_mud/384871_left_gravel_footstep_2.mp3 | Left Gravel Footstep 2 | Ali_6868 | https://freesound.org/s/384871/ | CC0 1.0 |
| footsteps_mud/384875_left_gravel_footstep_4.mp3 | Left Gravel Footstep 4 | Ali_6868 | https://freesound.org/s/384875/ | CC0 1.0 |
| footsteps_mud/166511_dirt_gravel_footstep_1.mp3 | dirt/gravel footstep 1 | Yoyodaman234 | https://freesound.org/s/166511/ | CC0 1.0 |
| footsteps_mud/166510_dirt_gravel_footstep_2.mp3 | dirt/gravel footstep 2 | Yoyodaman234 | https://freesound.org/s/166510/ | CC0 1.0 |
| wind_night/773075_wind_in_corn_field_on_20m_0305_p.mp3 | wind in corn field on  20m 0305 PM 24081 | klankbeeld | https://freesound.org/s/773075/ | CC BY 4.0 |
| wind_night/540035_spring_field_nl_02_200529_0183.mp3 | spring field NL 02 200529_0183.wav | klankbeeld | https://freesound.org/s/540035/ | CC BY 4.0 |
| crickets_night/715682_130819_night_insects_crickets_co.mp3 | 130819 Night, insects crickets, cottage  | TRP | https://freesound.org/s/715682/ | CC0 1.0 |
| crickets_night/574670_night_insects_thick_bright_pei_s.mp3 | Night insects thick bright PEI Summer 03 | TRP | https://freesound.org/s/574670/ | CC0 1.0 |
| crickets_night/484034_cricket_close_recording_erni.mp3 | Cricket, close recording: Erni | felix.blume | https://freesound.org/s/484034/ | CC0 1.0 |
| ratchet_click/151778_clip_in.mp3 | Clip In.wav | slappy13 | https://freesound.org/s/151778/ | CC0 1.0 |
| ratchet_click/117087_seatbelt_click.mp3 | seatbelt_click.wav | joedeshon | https://freesound.org/s/117087/ | CC BY 4.0 |
| ratchet_click/384406_seat_belt_unbuckle_buckling.mp3 | Seat belt unbuckle & buckling | Caitlin_100 | https://freesound.org/s/384406/ | CC0 1.0 |
| marker_write/383872_permanent_marker_writing.mp3 | Permanent Marker Writing | deleted_user_7146007 | https://freesound.org/s/383872/ | CC0 1.0 |
| marker_write/119926_marker_writing_on_paper.mp3 | Marker Writing on Paper.aif | ftpalad | https://freesound.org/s/119926/ | CC0 1.0 |
| blood_squirt/737235_water_squirt.mp3 | Water Squirt | qubodup | https://freesound.org/s/737235/ | CC0 1.0 |
| blood_squirt/414744_shortsquirt_1.mp3 | shortSquirt_1.mp3 | JeffNevington | https://freesound.org/s/414744/ | CC BY 4.0 |
| blood_squirt/414747_shortsquirt_3.mp3 | shortSquirt_3.mp3 | JeffNevington | https://freesound.org/s/414747/ | CC BY 4.0 |
| blood_squirt/537919_water_squirt_single_long.mp3 | Water Squirt Single Long.mp3 | belanhud | https://freesound.org/s/537919/ | CC0 1.0 |
| rain_trench/96729_light_rain.mp3 | Light rain.wav | Ryding | https://freesound.org/s/96729/ | CC0 1.0 |
| rain_trench/691544_rain_light_traffic_07.mp3 | rain_light_traffic_07.wav | ania635 | https://freesound.org/s/691544/ | CC0 1.0 |
| jet_flyby/189446_jet_plane_flyby.mp3 | Jet Plane Flyby.flac | qubodup | https://freesound.org/s/189446/ | CC0 1.0 |
| jet_flyby/431206_airplane_flyby.mp3 | Airplane Flyby | oscaraudiogeek | https://freesound.org/s/431206/ | CC0 1.0 |
| heli_flyby/241190_helicopter_flyby_pass.mp3 | Helicopter Flyby / Pass | mil0001 | https://freesound.org/s/241190/ | CC0 1.0 |
| heli_flyby/534855_helicopter_flyby.mp3 | Helicopter Flyby | m_cel | https://freesound.org/s/534855/ | CC0 1.0 |
| heli_flyby/467225_helicopter_flyby.mp3 | Helicopter flyby | derjuli | https://freesound.org/s/467225/ | CC0 1.0 |
| tank_cannon/127845_tank_fire_mixed.mp3 | Tank fire Mixed.wav | GaryQ | https://freesound.org/s/127845/ | CC0 1.0 |
| tank_cannon/448002_cannon.mp3 | cannon.mp3 | Kneeling | https://freesound.org/s/448002/ | CC0 1.0 |
| mg_burst/165394_heavy_machine_gun.mp3 | Heavy Machine Gun | ShawnyBoy | https://freesound.org/s/165394/ | CC0 1.0 |
| mg_burst/39071_heavy_burst.mp3 | heavy.burst.wav | alienbomb | https://freesound.org/s/39071/ | CC0 1.0 |
| mg_burst/342184_biggun.mp3 | BigGun.wav | Willary1 | https://freesound.org/s/342184/ | CC0 1.0 |
| mg_burst/165393_machine_gun_shots_fast.mp3 | Machine Gun Shots Fast | ShawnyBoy | https://freesound.org/s/165393/ | CC0 1.0 |
| bullet_crack/386854_bullet_crack.mp3 | Bullet Crack.wav | morganpurkis | https://freesound.org/s/386854/ | CC0 1.0 |
| bullet_crack/123222_bullet_whiz.mp3 | Bullet_whiz.wav | CGEffex | https://freesound.org/s/123222/ | CC BY 4.0 |
| impact_concrete/30932_bullet_ricochet.mp3 | bullet ricochet.wav | aust_paul | https://freesound.org/s/30932/ | CC0 1.0 |
| impact_concrete/148840_ricochet.mp3 | ricochet.mp3 | cedarstudios | https://freesound.org/s/148840/ | CC0 1.0 |
| impact_concrete/392975_ricochet_2.mp3 | Ricochet 2.wav | morganpurkis | https://freesound.org/s/392975/ | CC0 1.0 |
| impact_concrete/523403_22_caliber_with_ricochet.mp3 | 22 caliber with ricochet.wav | C-V | https://freesound.org/s/523403/ | CC0 1.0 |
| rocket_launch/840511_rocket_launch.mp3 | Rocket Launch | qubodup | https://freesound.org/s/840511/ | CC0 1.0 |
| rocket_launch/854476_m142_himars_rocket_launch_8.mp3 | M142 HIMARS Rocket Launch 8 | qubodup | https://freesound.org/s/854476/ | CC0 1.0 |
| smoke_grenade/551429_gas_grenade.mp3 | Gas Grenade.wav | Themiwa100 | https://freesound.org/s/551429/ | CC0 1.0 |
| pin_pull/210213_pin_pullout.mp3 | Pin Pullout.wav | Augdog | https://freesound.org/s/210213/ | CC BY 4.0 |
| artillery_rumble/486027_r12_31_artillery_guns_firing.mp3 | R12-31-Artillery Guns Firing.wav | craigsmith | https://freesound.org/s/486027/ | CC0 1.0 |
| artillery_rumble/675609_s16_31_bombardment_with_distant.mp3 | S16-31 Bombardment with distant explosio | craigsmith | https://freesound.org/s/675609/ | CC0 1.0 |
| tank_engine/187676_driving_tank_engine.mp3 | Driving Tank Engine.flac | qubodup | https://freesound.org/s/187676/ | CC0 1.0 |
| battle_bed/385895_battlefield_ambience_loop.mp3 | BattleField Ambience (Loop) | banan_ | https://freesound.org/s/385895/ | CC0 1.0 |
| bomb_large/259300_huge_explosion.mp3 | Huge Explosion | unfa | https://freesound.org/s/259300/ | CC0 1.0 |
| bomb_large/149966_muffled_distant_explosion.mp3 | Muffled Distant Explosion | NenadSimic | https://freesound.org/s/149966/ | CC0 1.0 |
| bomb_large/366091_huge_explosion_part_1_shockwave.mp3 | Huge Explosion Part 1 - Shockwave | bevibeldesign | https://freesound.org/s/366091/ | CC0 1.0 |
<!-- SFX-END -->

<!-- V2-ASSETS-BEGIN -->
## V2 models (stage w1)

BlenderKit models: free plan, BlenderKit licence (Royalty Free, or CC0 where noted). They are NOT in the public repo (`Assets/_Licensed/` is git-ignored); `python tools/bk_fetch.py get ...` re-creates them (commands in tools/bk_assets.json).

| Asset (slug) | Title | Author | Source | License |
|---|---|---|---|---|
| aircraft/ch47_helicopter | Chinhook CH-47 military helicopter | 3DAssets Kit | https://www.blenderkit.com/get-blenderkit/a08202f6-dadb-4e8c-95ca-107225bfa575/ | BlenderKit Royalty Free |
| aircraft/f16_jet | F-16 fighter jet | Joe Naaman | https://www.blenderkit.com/get-blenderkit/2b0e1d40-8487-4b92-8594-6141f2ec6162/ | BlenderKit Royalty Free |
| aircraft/fpv_drone_bomb | FPV Drone with a bomb | Dovydas Alytas | https://www.blenderkit.com/get-blenderkit/1efbef44-2746-41f3-95c4-e37fcd7e14cc/ | BlenderKit Royalty Free |
| aircraft/recon_drone_mavic | DJI Mavic 3 Classic Drone | Dovydas Alytas | https://www.blenderkit.com/get-blenderkit/bcbafb5a-a09f-4218-a05e-7b784476929d/ | BlenderKit Royalty Free |
| aircraft/utility_helicopter | ALH Dhruv Helicopter | Ankur Chauhan | https://www.blenderkit.com/get-blenderkit/6a48a9a9-fc54-4255-b45d-1cbc6f08b4a5/ | BlenderKit Royalty Free |
| cover/barbed_wire_fence | Barbed wire fence | Zoltán Péter Fáth | https://www.blenderkit.com/get-blenderkit/b4e9bc00-6747-4454-83ca-aecd68d3bc29/ | CC0 1.0 |
| cover/barbed_wire_metal_fence | Barb Wire Metal Fence | Texture Maker | https://www.blenderkit.com/get-blenderkit/f15f9631-254d-461f-a84f-507358605040/ | BlenderKit Royalty Free |
| cover/burlap_sacks | 50kg burlap sacks of grain | Dovydas Alytas | https://www.blenderkit.com/get-blenderkit/4636c678-151c-444a-86ac-a1d21f477456/ | BlenderKit Royalty Free |
| cover/concrete_barriers_lowpoly | Lowpoly concrete barriers | SPLEEN.VISION Adam | https://www.blenderkit.com/get-blenderkit/909bf29c-d645-46c0-a5bb-dade7d1a9e44/ | CC0 1.0 |
| cover/concrete_blockade | Concrete Blockade | Jan Hecl | https://www.blenderkit.com/get-blenderkit/87212614-0aa3-44a8-bf79-8e6d2ca6e52f/ | BlenderKit Royalty Free |
| cover/concrete_pipe | Concrete Pipe | REAL SCANS | https://www.blenderkit.com/get-blenderkit/580268f0-505b-4c20-a414-2c3074a3215c/ | BlenderKit Royalty Free |
| cover/concrete_road_barrier | Concrete Road Barrier | Poly Haven | https://www.blenderkit.com/get-blenderkit/1da29004-f3c0-4bc7-a675-fd6e500c2aa7/ | CC0 1.0 |
| cover/czech_hedgehog | Anti-Tank Barrier | Giorgi Samukashvili | https://www.blenderkit.com/get-blenderkit/483c7ea3-dcd8-4b65-8e41-e88b15b6b06c/ | BlenderKit Royalty Free |
| props/ammo_556 | Ammunition caliber 5.56x45mm | BlenderBot | https://www.blenderkit.com/get-blenderkit/e5b19cec-b676-46de-a1c6-b7dc44e23ac8/ | BlenderKit Royalty Free |
| props/ammo_box | Ammo Box | Poly Haven | https://www.blenderkit.com/get-blenderkit/204a699e-273b-494a-a567-4f340491a451/ | CC0 1.0 |
| props/artillery_shells | Artillery shells | Dovydas Alytas | https://www.blenderkit.com/get-blenderkit/617d6057-c252-4312-8d06-715d81012e1f/ | BlenderKit Royalty Free |
| props/fuel_drums | WW2 Italian Military Fuel-Drum Stack | Mad Lobster Workshop Dolzall | https://www.blenderkit.com/get-blenderkit/1d7fcb2c-f4fa-46c3-a6a1-865dab5a737b/ | BlenderKit Royalty Free |
| props/jerrycan_green | Metal Jerrycan Green | Poly Haven | https://www.blenderkit.com/get-blenderkit/c3b66b2c-07d3-4818-ba03-e068d6a9db59/ | CC0 1.0 |
| props/kz_flag_stand | Kazakhstan Ceremonial flag on stand | Demycs | https://www.blenderkit.com/get-blenderkit/08c7dadc-99a3-48ca-b5e0-501c6bd5faaf/ | BlenderKit Royalty Free |
| props/military_crate | Wooden Military Crate | Poly Haven | https://www.blenderkit.com/get-blenderkit/ffae7880-9e4f-42e0-a61e-d0a5e1a1c766/ | CC0 1.0 |
| props/military_tent | Military Tent (2-Man) | Harrison Costa | https://www.blenderkit.com/get-blenderkit/e8ae0437-a080-4601-b734-75032d209008/ | BlenderKit Royalty Free |
| props/radio_r311 | Military Radio R311 Omega | Demycs | https://www.blenderkit.com/get-blenderkit/06155894-2673-4a98-b56f-e648793f7924/ | BlenderKit Royalty Free |
| props/smoke_grenade | Flashbang | Blaž Mraz | https://www.blenderkit.com/get-blenderkit/718de840-4cab-4663-9bc7-99e778b77039/ | BlenderKit Royalty Free |
| props/stretcher | Ambulance strechers | Dovydas Alytas | https://www.blenderkit.com/get-blenderkit/0e53d37b-e72e-46d7-8670-121925c19563/ | BlenderKit Royalty Free |
| ruins/brick_block_wall | Brick and Concrete Block Wall | GL3D Gabriela Linhares | https://www.blenderkit.com/get-blenderkit/818e29a3-d812-402d-a10c-5ee71d85f2fc/ | BlenderKit Royalty Free |
| ruins/brick_ruins_a | Brick Ruins | PBRPX | https://www.blenderkit.com/get-blenderkit/80cd0314-d86c-4c92-8186-4151a07e9522/ | CC0 1.0 |
| ruins/brick_ruins_b | Brick Ruins | PBRPX | https://www.blenderkit.com/get-blenderkit/f0a80792-ff43-4dce-a369-b2db68360937/ | CC0 1.0 |
| ruins/brick_ruins_c | Brick Ruins | PBRPX | https://www.blenderkit.com/get-blenderkit/33363fe7-cfc1-482c-a8d5-c2a51fdedd92/ | CC0 1.0 |
| ruins/bricks_debris_pile | Bricks Derbis Pile | REAL SCANS | https://www.blenderkit.com/get-blenderkit/463dbfc1-8826-4615-ba79-92b89d4e3520/ | BlenderKit Royalty Free |
| ruins/broken_wall | Broken Wall | PBRPX | https://www.blenderkit.com/get-blenderkit/e5d3e731-00a3-4987-9dff-a787026218ed/ | CC0 1.0 |
| ruins/concrete_chunk | A piece of concrete | Matvey Sheyanov | https://www.blenderkit.com/get-blenderkit/288214cb-6fa2-443b-a6e3-71dbe88dd886/ | BlenderKit Royalty Free |
| ruins/debris_bricks_wall | Debris of bricks and a wall | Matvey Sheyanov | https://www.blenderkit.com/get-blenderkit/e8841ea4-a9b9-49fe-8403-0bf80e807183/ | BlenderKit Royalty Free |
| ruins/derelict_concrete_building | Derelict Concrete Building | Fridqeir Haynura | https://www.blenderkit.com/get-blenderkit/e381565d-0afc-4125-acfd-abde83625e8e/ | BlenderKit Royalty Free |
| ruins/junk_slab_pile | Pile of Junk (rubbish, debris) Photoscan | Tedd Šámal | https://www.blenderkit.com/get-blenderkit/029d6806-b86b-47ef-8a87-16b868219c7e/ | BlenderKit Royalty Free |
| ruins/pillar_bullet_holes | Pillar scan | REAL SCANS | https://www.blenderkit.com/get-blenderkit/21d82a38-02f6-43d7-b145-ca9309c13d4b/ | BlenderKit Royalty Free |
| ruins/rubble_pile | Rubble Pile | Guinter Ramos | https://www.blenderkit.com/get-blenderkit/68d9a4ac-5bc0-46f1-8175-42ac843c37b8/ | BlenderKit Royalty Free |
| ruins/ruined_masonry_wall | Ruined Masonry Wall | REAL SCANS | https://www.blenderkit.com/get-blenderkit/858d9662-d036-4d93-80c1-d0e1e06143ab/ | BlenderKit Royalty Free |
| ruins/unfinished_building | Unfinished Building | AIR3D | https://www.blenderkit.com/get-blenderkit/694baa4b-b62c-459b-b5d3-6429c8ceff76/ | BlenderKit Royalty Free |
| soldiers/sniper_soldier | Special Sniper Soldier-Rigged-Game Ready | Silent Wolf | https://www.blenderkit.com/get-blenderkit/bab061a6-ed60-4c1d-9465-d501700264b7/ | BlenderKit Royalty Free |
| soldiers/soldier_agent | Special Sniper Soldier-Rigged-Game Ready | Silent Wolf | https://www.blenderkit.com/get-blenderkit/bab061a6-ed60-4c1d-9465-d501700264b7/ | BlenderKit Royalty Free |
| soldiers/soldier_crowd | Special Sniper Soldier-Rigged-Game Ready | Silent Wolf | https://www.blenderkit.com/get-blenderkit/bab061a6-ed60-4c1d-9465-d501700264b7/ | BlenderKit Royalty Free |
| soldiers/soldier_far | Special Sniper Soldier-Rigged-Game Ready | Silent Wolf | https://www.blenderkit.com/get-blenderkit/bab061a6-ed60-4c1d-9465-d501700264b7/ | BlenderKit Royalty Free |
| vehicles/arx_apc | Arx apc | Ermia J | https://www.blenderkit.com/get-blenderkit/082cab8b-8354-4c46-9e6c-ad652f7c075e/ | BlenderKit Royalty Free |
| vehicles/car_wreck_beetle | VW Beetle wreck | Adam Krhanek | https://www.blenderkit.com/get-blenderkit/ce96377d-5da3-4899-bb25-c94b84bb0de7/ | BlenderKit Royalty Free |
| vehicles/m1a2_tank | M1A2 Abrams US Tank | AIR3D | https://www.blenderkit.com/get-blenderkit/6ff87b83-2b1b-43fb-a5a8-0d87b55741c6/ | BlenderKit Royalty Free |
| vehicles/pinzgauer_truck | Pinzgauer | Ermia J | https://www.blenderkit.com/get-blenderkit/f71a3c39-7279-4206-ae19-d5b861318f48/ | BlenderKit Royalty Free |

Poly Haven (CC0 1.0, committed in Assets/ThirdParty/PolyHaven):

| type | asset | authors | url | license |
|---|---|---|---|---|
| HDRI 2k | Overcast Soil (Pure Sky) (overcast_soil_puresky) | Jarod Guest, Sergej Majboroda | https://polyhaven.com/a/overcast_soil_puresky | CC0 1.0 |
| texture 1k | Withered Grass (withered_grass) | Charlotte Baglioni | https://polyhaven.com/a/withered_grass | CC0 1.0 |
| texture 1k | Sparse Grass (sparse_grass) | Amal Kumar | https://polyhaven.com/a/sparse_grass | CC0 1.0 |
| texture 1k | Dry Ground 01 (dry_ground_01) | Rob Tuytel | https://polyhaven.com/a/dry_ground_01 | CC0 1.0 |
| texture 1k | Dry Ground Rocks (dry_ground_rocks) | Rob Tuytel | https://polyhaven.com/a/dry_ground_rocks | CC0 1.0 |
| texture 1k | Brown Mud Dry (brown_mud_dry) | Rob Tuytel | https://polyhaven.com/a/brown_mud_dry | CC0 1.0 |
| texture 1k | Rough Plaster Brick 04 (rough_plaster_brick_04) | Rob Tuytel | https://polyhaven.com/a/rough_plaster_brick_04 | CC0 1.0 |
| texture 1k | Broken Brick Wall (broken_brick_wall) | Amal Kumar | https://polyhaven.com/a/broken_brick_wall | CC0 1.0 |
| texture 1k | Red Bricks 04 (red_bricks_04) | Rob Tuytel | https://polyhaven.com/a/red_bricks_04 | CC0 1.0 |
| texture 1k | Concrete Floor Damaged 01 (concrete_floor_damaged_01) | Rob Tuytel | https://polyhaven.com/a/concrete_floor_damaged_01 | CC0 1.0 |
| texture 1k | Burned Ground 01 (burned_ground_01) | Rob Tuytel | https://polyhaven.com/a/burned_ground_01 | CC0 1.0 |
| texture 1k | Concrete Debris (concrete_debris) | Amal Kumar | https://polyhaven.com/a/concrete_debris | CC0 1.0 |
| model (rocks) | Rock 07 (rock_07) | Jenelle van Heerden | https://polyhaven.com/a/rock_07 | CC0 1.0 |
| model (rocks) | Rock 09 (rock_09) | Jenelle van Heerden | https://polyhaven.com/a/rock_09 | CC0 1.0 |
| model (rocks) | Namaqualand Boulder 02 (namaqualand_boulder_02) | Greg Zaal, Rico Cilliers | https://polyhaven.com/a/namaqualand_boulder_02 | CC0 1.0 |
| model (rocks) | Sand Rocks Small 01 (sand_rocks_small_01) | Rob Tuytel, Rico Cilliers | https://polyhaven.com/a/sand_rocks_small_01 | CC0 1.0 |
| model (vegetation) | Grass Medium 01 (grass_medium_01) | Rob Tuytel, Rico Cilliers | https://polyhaven.com/a/grass_medium_01 | CC0 1.0 |
| model (props) | Old Tyre (old_tyre) | MP | https://polyhaven.com/a/old_tyre | CC0 1.0 |
| texture 1k | Hessian 230 (hessian_230) | colormass, Rico Cilliers | https://polyhaven.com/a/hessian_230 | CC0 1.0 |
<!-- V2-ASSETS-END -->
