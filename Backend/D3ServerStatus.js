#!/usr/bin/node

// Library
const net = require('net');
const redis = require('ioredis');

// Server listen port
const PORT = 3000;

// Default redis entry TTL
const TTL = 3600 * 24;

// IP regex
const regex_IP = /^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$/;

// BattleTAG regex
const regex_BTAG = /^[ABCDEF0-9]{8}$/;

// Redis client
const redisClient = redis.createClient({
	host: 'localhost',
	port: 6379,
});

// Server application
const server = net.createServer((socket) => {

	// Client connected
	// console.log('Client connected ...');

	// On data received
	socket.on('data', (data) => {

		try {
			// Parse msg data
			const jsonData = JSON.parse(data.toString());
			const { cmd, serverIP, battletag, rating } = jsonData;

			// Debug input json
			console.log(jsonData);

			// Check cmd
			if (!cmd || (cmd !== "GET" && cmd !== "POST")) {
				console.log('Invalid cmd param: cmd');
				socket.write(JSON.stringify( { result: 'KO', error: 'Invalid cmd param' } ));
				return;
			}

			// Cmd: GET
			if (cmd==="GET") {
				// Check server ip param
				if (!serverIP || !regex_IP.test(serverIP)) {
					console.log('Invalid serverIP param');
					socket.write(JSON.stringify( { result: 'KO', error: 'Invalid serverIP param' } ));
					return;
				}			

				// Redis qry: all server rates
				const redisSearchString = `server:${serverIP}:*`;

				// Find all keys
				redisClient.keys(redisSearchString, (err, keys) => {
					if (err) {
						console.log('Redis Server Error');
						socket.write(JSON.stringify( { result: 'KO', error: 'Redis Server Error' } ));
						return;
					}

					// no rates for this server
					if (keys.length === 0) {
						socket.write(JSON.stringify( { result: 'OK', rating: 0, nvotes: 0, voted: false } ));
						return;
					}									

					// mget all values
					redisClient.mget(keys, (err, ratings) => {
						if (err) {
							console.log('Redis Server Error');
							socket.write(JSON.stringify( { result: 'KO', error: 'Redis Server Error' } ));
							return;
						}

						// Calculate rating
						const ratingSum = ratings.reduce((acc, curr) => acc + parseInt(curr), 0);
						const ratingAvg = ratingSum / ratings.length;
						const score = Math.round(ratingAvg.toFixed(2));
						
						// User already voted this server?
						const voted = keys.includes(`server:${serverIP}:${battletag}`);
											
						console.log(`GET: ${serverIP}:${score}:${ratings.length}:${voted} ...`);
						socket.write(JSON.stringify( { result: 'OK', rating: score, nvotes: ratings.length, voted: voted } ));
						return;
					});
				});
			}

			// Cmd: POST
			if (cmd==="POST") {

				// Check params
				if (	(!serverIP || !regex_IP.test(serverIP)) ||
						(!battletag || !regex_BTAG.test(battletag)) ||
						(!rating || isNaN(rating) || rating < 1 || rating > 4) ) {
					console.log('Invalid params');
					socket.write(JSON.stringify( { result: 'KO', error: 'Invalid params' } ));
					return;
				}

				// redis add entry
				redisClient.set(`server:${serverIP}:${battletag}`, rating, (err, reply) => {
					if (err) {
						console.log('Redis Server Error');
						socket.write(JSON.stringify( { result: 'KO', error: 'Redis Server Error' } ));
						return;
					}

					// set TTL
					// redisClient.expire(`server:${serverIP}:${battletag}`, TTL);

					// write result
					console.log(`POST: ${serverIP}:${battletag}:${rating} ...`);
					socket.write(JSON.stringify( { result: 'OK' } ));
					return;
				});
			}
		} catch (err) {
			socket.write(JSON.stringify( { result: 'KO', error: 'Protocol Error' } ));
			console.log(err);
		}
	});

	// Client error
	socket.on('error', function(e) {
		console.log('Socket error:', e);
	});

	// Client disconnected
	socket.on('close', () => {
		// console.log('Client disconnected.');
	});
});

// Start server
server.listen(PORT, () => {
	console.log(`Server listen on port: ${PORT} ...`);
}).on('error', function(e) {
	console.log('Server error:', e);
});
