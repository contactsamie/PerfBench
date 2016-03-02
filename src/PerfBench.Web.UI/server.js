import express from 'express';

let app = express();

app.use(express.static('public'));

(async () => {
	try {

	app.listen(3000, () => console.log('Running'));	

	} catch(err) {
		console.log(err);
	}
})();
