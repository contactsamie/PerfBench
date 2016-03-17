module.exports = {
	entry: "./public/js/app.js",
	devtool: 'source-map',
	output: {
		path: __dirname + "/public",
		filename: "bundle.js"
	},
	module: {
		loaders: [
			{ test:/\.js$/, exclude: /node_modules/, loader: "babel-loader",
			  query: {
					presets: ['react', 'es2015', 'stage-0'],
					plugins: ["syntax-object-rest-spread", "transform-object-rest-spread"]

				}
			}
		]
	}
}
