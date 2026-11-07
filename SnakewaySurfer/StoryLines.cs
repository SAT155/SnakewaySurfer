namespace SnakewaySurfer;

// What the ravens say (SurfStory.cs). Lines starting "H:" are Hugin's, "M:" Munin's. A burst is
// one or two lines said together; "~N" instead of a burst is N minutes of silence.
internal static class StoryLines
{
	// Hugin, as you come out of the strange portal and find yourself where you were.
	public static readonly string[][] Arrival =
	{
		new[] { "H:Hail, warrior! Oh. You've come out exactly where you went in. How... unremarkable." },
		new[] { "H:You look tired. Very tired. Have you thought about a bed? I think about beds for you." },
	};

	// Nagging, politest first. Picked from the tier for how long you've been awake.
	public static readonly string[][] Polite =
	{
		new[] { "H:The ancestors rested when the sun went down. Just a thought. A wise, ancient thought." },
		new[] { "H:A bed is a simple thing. Wood, a fur, a roof. You have built far harder." },
		new[] { "H:The Allfather wants you rested. Well. I want you rested. He'd agree if he were listening." },
		new[] { "H:Sleep restores the body. I'm told. By people who sleep." },
		new[] { "H:I flew all this way with one message. It is: sleep. That was the whole message." },
		new[] { "H:You're doing very well. You'd do even better lying down with your eyes closed." },
		new[] { "H:A fur, a pillow, a crackling fire... Sorry, I was describing your bed. You remember it?" },
	};

	public static readonly string[][] Snide =
	{
		new[] { "H:Still awake? Bold. Foolish, but bold." },
		new[] { "H:Those dark rings under your eyes are not war paint." },
		new[] { "H:I watched you hit the same tree four times. Four. Sleep." },
		new[] { "H:Even the Greydwarfs nap. The Greydwarfs." },
		new[] { "H:Your bed sent word. It misses you. I'm only the messenger." },
		new[] { "H:Each day you don't sleep, a raven loses a feather. Look at me. LOOK AT ME." },
		new[] { "H:It isn't a trap. Why would a bed be a trap? Lie down. Nothing will happen. Probably." },
		new[] { "H:You know who never slew a troll? Someone who skipped sleep. Probably. I haven't checked." },
		new[] { "H:I have counted your blinks. They are getting longer. Soon I win." },
	};

	public static readonly string[][] Rude =
	{
		new[] { "H:Fine. Stay up. See if I care. ...I care. Go to bed." },
		new[] { "H:You swing that axe like a seagull with a grudge." },
		new[] { "H:I asked the Allfather about you. He sighed. Do you know how long a god sighs?" },
		new[] { "H:BED. I am out of polite words and running low on rude ones." },
		new[] { "H:This is no longer a suggestion. It is a raven's demand." },
		new[] { "H:Somewhere a bed lies cold and lonely, and it's your fault." },
		new[] { "H:What are you afraid of? Dreams? ...Don't answer that." },
		new[] { "H:You smell like someone who hasn't slept. I'm a raven. I like that smell. Stop it anyway." },
	};

	// Said at night (instead of a tier line, some of the time).
	public static readonly string[][] Night =
	{
		new[] { "H:The sun has gone to bed. Take the hint." },
		new[] { "H:It's dark. Do you know what dark is for? It's for sleeping. That's its whole job." },
		new[] { "H:Night. Stars. Crickets. Everything in this world is telling you something." },
	};

	// When you finally lie down.
	public static readonly string[][] Bed =
	{
		new[] { "H:FINALLY. Sweet dreams, warrior. Mind the road." },
		new[] { "H:Yes. Yes! Close your eyes. Don't think about running. Definitely don't dream of running." },
	};

	public static readonly string[][] BedTogether =
	{
		new[] { "M:It's lying down! Hugin, it's lying down!", "H:Don't. Move. Don't spook it." },
		new[] { "M:Kraa! Is this the season finale?", "H:Shh! Sleep well, friend. Our dear, dear friend." },
	};

	// Munin turns up after a day without sleep, and the two of them make a show of it. Played
	// in order, a burst every few minutes; "~N" is a pause of N minutes.
	public static readonly string[][][] Episodes =
	{
		// Munin arrives.
		new[]
		{
			new[] { "M:Kraa! Brother. Why are you shouting at that one?", "H:It won't sleep, Munin. A whole day now." },
			new[] { "M:Have you tried asking nicely?", "H:I have tried EVERYTHING." },
			new[] { "M:Have you tried asking rudely?", "H:...I'll admit that went better." },
			new[] { "M:Kraa! I know this one. It's the one that hit a Troll with a club made of another club." },
			new[] { "H:Munin is my lesser brother.", "M:Younger. YOUNGER brother." },
		},
		// The podcast.
		new[]
		{
			new[] { "H:Welcome to Two Birds, One Stone, the only podcast in the tenth world.", "M:Kraa! And the only one with feathers." },
			new[] { "M:Today's guest: a warrior who has never heard of a bed.", "H:Allegedly heard of. Never used." },
			new[] { "H:A listener writes in: why won't it sleep?", "M:Great question, listener. We have no idea." },
			new[] { "M:Theory one: it fears the dark.", "H:It once slept beside a burning fire. Theory rejected." },
			new[] { "H:Theory two: it is secretly an owl.", "M:It has no feathers.", },
			new[] { "H:That's exactly what an owl would want us to think." },
			new[] { "M:This episode was brought to you by Beds.", "H:Beds. You lie on them. Like this one should." },
			new[] { "H:That's all for today.", "M:Like, subscribe, and LIE DOWN." },
		},
		// Fun facts.
		new[]
		{
			new[] { "M:Fun fact: the Allfather hasn't slept in a thousand years.", "H:And look how he turned out. One eye. Hangs off trees." },
			new[] { "M:...Point taken." },
			new[] { "H:Fun fact: a well-rested warrior carries half again as many stones.", "M:Is that true?" },
			new[] { "H:It is now." },
			new[] { "M:Fun fact: a Neck has never once been tired.", "H:Because it sleeps in a puddle all day. Learn from the Neck." },
			new[] { "H:Fun fact: Bonemass has been resting for centuries.", "M:And look how strong he is! Be like Bonemass." },
			new[] { "M:Hugin, I don't think we should tell it to be like Bonemass." },
		},
		// Gaslighting.
		new[]
		{
			new[] { "H:Remember when you promised to sleep at sundown?", "M:It did promise. I heard it." },
			new[] { "H:You don't remember? Hm. Memory is the first thing to go." },
			new[] { "M:Kraa! Memory is MY department. And I don't remember you ever sleeping." },
			new[] { "H:Have you noticed the trees getting taller?", "M:They aren't. That's the tiredness." },
			new[] { "M:Your bed told us what you said about it.", "H:Something hurtful. We won't repeat it." },
			new[] { "H:That deer just called you sleepy.", "M:I heard it too. Rude deer." },
			new[] { "M:Wasn't it night just a moment ago?", "H:It was. You missed it. Again." },
		},
		// An argument (Munin's way with the Dvergr).
		new[]
		{
			new[] { "M:In my day we got warriors to do sensible things. Like go and fight the Dvergr.", "H:That wasn't sensible, Munin." },
			new[] { "M:Effective, though." },
			new[] { "H:Maybe it needs a reason. Warrior: the Dvergr are asleep right now. All of them.", "M:Mocking you. In their sleep." },
			new[] { "H:I'm the one who sits on the right shoulder.", "M:And I sit on the left. The good shoulder." },
			new[] { "H:There is no good shoulder, Munin.", "M:Then why does he lean my way?" },
			new[] { "M:When it dies, I still get the right eye.", "H:We agreed I'd get the right one if it died tired." },
			new[] { "H:Fine. You take both eyes. I'll take the satisfaction of having been right." },
		},
		// The long break.
		new[]
		{
			new[] { "H:Munin and I are going to take a short break.", "M:A very short one. Don't do anything while we're gone." },
			new[] { "~25" },
			new[] { "H:...", "M:..." },
			new[] { "~20" },
		},
		// Back, and best friends.
		new[]
		{
			new[] { "H:We're back! Did you miss us? You missed us.", "M:Kraa! It missed us." },
			new[] { "M:We've been talking, and we've decided you're our best friend.", "H:Our very best friend. Our only friend, technically." },
			new[] { "H:And best friends tell each other things.", "M:Like: you look awful. Go to sleep." },
			new[] { "M:We made you something.", "H:It's a pillow. It's imaginary. Use a real one." },
			new[] { "H:Best friends sleep at the same time, friend.", "M:We'll sleep when you sleep. Ravens' honour." },
			new[] { "M:Hugin is asleep.", "H:I am NOT asleep. I'm resting my eye. Both of them." },
			new[] { "H:You know what friends do? They go to bed when their friends ask them.", "M:Best friends do it the first time." },
		},
		// Desperation.
		new[]
		{
			new[] { "M:What if we sing it to sleep?", "H:No." },
			new[] { "M:Hush little warrior, don't say a word...", "H:Munin. Please." },
			new[] { "M:...Allfather's gonna buy you a mead-hall bird...", "H:A bird?! Munin, that's us. He'd be buying US." },
			new[] { "H:Counting sheep works. I've heard. There are no sheep. Count boars.", "M:One boar. Two boar. Kraa! Three boar..." },
			new[] { "M:...forty boar. Forty-one...", "H:Is it working?" },
			new[] { "M:It's chopping a tree.", "H:Of course it is." },
		},
		// Reverse psychology.
		new[]
		{
			new[] { "H:New plan. Warrior: whatever you do, DO NOT sleep.", "M:Yes. It is forbidden. Beds are very dangerous." },
			new[] { "M:Only the bravest warriors dare to lie down.", "H:And you're not brave, are you? Are you?" },
			new[] { "H:Is it working?", "M:It's looking at a mushroom." },
			new[] { "H:Fine. FINE. Stay awake forever. Live in the woods. Become a Greydwarf.", "M:I'd watch that." },
		},
		// The end of the season.
		new[]
		{
			new[] { "M:Hugin, are we... the ones who are tired now?", "H:Speak for yourself." },
			new[] { "H:I may have dozed off on a branch earlier.", "M:You fell off it." },
			new[] { "M:That was the season finale of Two Birds, One Stone.", "H:We'll be back next season. It will be about the same thing." },
			new[] { "H:Goodnight, friend. Take the hint.", "M:Kraa. Goodnight." },
		},
	};

	// After the last episode: reruns, at random.
	public static readonly string[][] Reruns =
	{
		new[] { "H:This is a rerun. We ran out of episodes. You did this." },
		new[] { "M:Previously on Two Birds, One Stone: it didn't sleep.", "H:And now, the thrilling continuation: it still doesn't." },
		new[] { "H:I've started sleeping in shifts so one of us is always watching you.", "M:Kraa! My shift." },
		new[] { "M:Do you think it knows that the bed is the square thing with the fur on it?", "H:I'll draw it a picture." },
		new[] { "H:We've told the Allfather.", "M:He said 'who?'. But we told him." },
		new[] { "M:Hugin bet me a fish you'd sleep tonight.", "H:Don't make me lose a fish, friend." },
	};

	// Dreams after the first (sleeping later on, if the dreams keep coming).
	public static readonly string[][] LaterDreams =
	{
		new[] { "You dream of the road again. It has missed you." },
		new[] { "Two ravens argue somewhere far above. The road runs on beneath them." },
		new[] { "The trees rush past like water. You know the way now." },
		new[] { "A voice that is not your own: run, and do not stop. And then, quieter: get some rest." },
	};
}
