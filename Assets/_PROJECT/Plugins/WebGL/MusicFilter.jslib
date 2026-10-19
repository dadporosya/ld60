// The music's filters and volume on the Web, where Unity's AudioLowPassFilter / AudioHighPassFilter and
// OnAudioFilterRead (BgmGainFilter) don't run. Driven by WebMusicFilter.cs.
//
// Unity plays every AudioSource through a channel in WEBAudio.audioInstances: source -> gain -> destination.
// Music channels are recognised by their spatial blend, which SongManager sets to WebMusicFilter.Tag, and get
// re-routed: gain -> [high-pass] -> [low-pass] -> music gain -> destination.
var MusicFilterLib = {
	$MusicFilter: {
		musicChannels: -1,

		// Adds this file's nodes to a channel, and keeps them in the graph whenever Unity rewires it.
		patch: function (channel) {
			var ctx = WEBAudio.audioContext;
			var f = {
				highPass: ctx.createBiquadFilter(),
				lowPass: ctx.createBiquadFilter(),
				gain: ctx.createGain(),
				chain: null,
				key: 'unrouted'
			};
			f.highPass.type = 'highpass';
			f.lowPass.type = 'lowpass';
			f.gain.connect(ctx.destination);
			channel.musicFilter = f;

			// Unity connects the channel's gain to the destination every time it (re)starts a clip.
			var configure = channel.configurePanningNodes;
			channel.configurePanningNodes = function () {
				configure.call(this);
				if (this.source && this.musicFilter.chain) MusicFilter.connect(this, this.musicFilter.chain);
			};

			var release = channel.release;
			channel.release = function () {
				release.call(this);
				f.highPass.disconnect();
				f.lowPass.disconnect();
				f.gain.disconnect();
			};
		},

		// Routes the channel's gain through `chain` (null: straight to the destination, as Unity has it).
		connect: function (channel, chain) {
			var f = channel.musicFilter;
			channel.gain.disconnect();
			f.highPass.disconnect();
			f.lowPass.disconnect();
			if (!chain) {
				channel.gain.connect(WEBAudio.audioContext.destination);
				return;
			}

			var node = channel.gain;
			for (var i = 0; i < chain.length; i++) {
				node.connect(chain[i]);
				node = chain[i];
			}
		},

		// Unity's resonance Q (1: no boost) as a Web Audio biquad Q (dB).
		resonanceDb: function (q) {
			return 20 * Math.log(Math.max(1, q)) / Math.LN10;
		},

		// Glides an AudioParam to `value` over a few ms, so a per-frame sweep doesn't click. The first value is set
		// at once, so a channel that just joined the music doesn't sweep in from the node's defaults.
		glide: function (param, value) {
			if (param.musicTarget === value) return;
			if (param.musicTarget === undefined) param.value = value;
			else param.setTargetAtTime(value, WEBAudio.audioContext.currentTime, 0.015);
			param.musicTarget = value;
		}
	},
	$MusicFilter__deps: ['$WEBAudio'],

	MusicFilter_Apply__deps: ['$MusicFilter', '$WEBAudio'],
	MusicFilter_Apply: function (tag, lowPassOn, lowCutoff, lowQ, highPassOn, highCutoff, highQ, gain) {
		if (!WEBAudio.audioWebEnabled || !WEBAudio.audioContext) return;

		var nyquist = WEBAudio.audioContext.sampleRate / 2;
		var count = 0;
		var instances = WEBAudio.audioInstances;
		for (var id in instances) {
			var channel = instances[id];
			// Sound clips live in the same table; only channels have panning nodes.
			if (!channel || typeof channel.configurePanningNodes !== 'function') continue;

			var music = Math.abs(channel.spatialBlendLevel - tag) < tag * 0.001;
			if (!music && !channel.musicFilter) continue;
			if (!channel.musicFilter) MusicFilter.patch(channel);

			var f = channel.musicFilter;
			var key = music ? (highPassOn ? 'h' : '') + (lowPassOn ? 'l' : '') + 'g' : 'unrouted';
			if (key !== f.key) {
				f.key = key;
				if (music) {
					f.chain = [];
					if (highPassOn) f.chain.push(f.highPass);
					if (lowPassOn) f.chain.push(f.lowPass);
					f.chain.push(f.gain);
				} else {
					f.chain = null;
				}
				MusicFilter.connect(channel, f.chain);
			}
			if (!music) continue;

			count++;
			MusicFilter.glide(f.highPass.frequency, Math.min(highCutoff, nyquist));
			MusicFilter.glide(f.highPass.Q, MusicFilter.resonanceDb(highQ));
			MusicFilter.glide(f.lowPass.frequency, Math.min(lowCutoff, nyquist));
			MusicFilter.glide(f.lowPass.Q, MusicFilter.resonanceDb(lowQ));
			MusicFilter.glide(f.gain.gain, gain);
		}

		if (count !== MusicFilter.musicChannels) {
			MusicFilter.musicChannels = count;
			console.log('MusicFilter: filtering ' + count + ' music channel(s).');
		}
	}
};

mergeInto(LibraryManager.library, MusicFilterLib);
